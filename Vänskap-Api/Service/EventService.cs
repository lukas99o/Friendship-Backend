using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Vänskap_Api.Data;
using Vänskap_Api.Models;
using Vänskap_Api.Models.Dtos.Event;
using Vänskap_Api.Service.IService;

namespace Vänskap_Api.Service
{
    public class EventService : IEventService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _contextAccessor;
        private readonly IWebHostEnvironment _enviroment;
        private string UserId => _contextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? throw new ArgumentNullException(nameof(UserId));
        private string UserName => _contextAccessor.HttpContext?.User.FindFirst(ClaimTypes.Name)?.Value ?? throw new ArgumentNullException(nameof(UserName)); 

        public EventService(ApplicationDbContext context, IHttpContextAccessor contextAssessor, IWebHostEnvironment enviroment)
        {
            _context = context;
            _contextAccessor = contextAssessor;
            _enviroment = enviroment;
        }

        public async Task<(ReadEventDto?, string?)> CreateEvent(EventDto createEvent)
        {
            var today = DateTime.UtcNow.Date;

            var eventsToday = await _context.Events
                .CountAsync(e =>
                    e.CreatedByUserId == UserId &&
                    e.CreatedAt >= today);

            if (eventsToday >= 5)
            {
                return (null, "Daily event limit reached");
            }

            var interests = new List<Interest>();
            if (createEvent.Interests != null)
            {
                interests = await _context.Interests
                    .Where(i => createEvent.Interests.Contains(i.Name))
                    .ToListAsync();
            }

            var createObj = new Event()
            {
                Title = createEvent.Title,
                Description = createEvent.Description,
                StartTime = createEvent.StartTime,
                CreatedByUserId = UserId,
                EndTime = createEvent.EndTime,
                AgeRangeMax = createEvent.AgeRangeMax,
                AgeRangeMin = createEvent.AgeRangeMin,
                IsPublic = createEvent.IsPublic,
                Location = createEvent.Location,
                EventInterests = new List<EventInterest>(),
                EventParticipants = new List<EventParticipant>()
            };

            await _context.Events.AddAsync(createObj);
            await _context.SaveChangesAsync();

            foreach (var interest in interests)
            {
                createObj.EventInterests.Add(new EventInterest
                {
                    InterestId = interest.Id,
                    EventId = createObj.Id 
                });
            }

            var eventParticipant = new EventParticipant()
            {
                Role = "Host",
                UserId = UserId,
                Event = createObj
            };
            createObj.EventParticipants.Add(eventParticipant);

            var conversation = new Conversation()
            {
                Title = $"Chat för {createObj.Title}"
            };

            var conversationParticipant = new ConversationParticipant()
            {
                UserId = UserId,
                Role = "Host",
                Conversation = conversation
            };
            conversation.ConversationParticipants.Add(conversationParticipant);
            createObj.Conversation = conversation;

            if (createEvent.EventPicture != null)
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
                var fileExtension = Path.GetExtension(createEvent.EventPicture.FileName).ToLower();
                if (!allowedExtensions.Contains(fileExtension))
                {
                    _context.Events.Remove(createObj);
                    await _context.SaveChangesAsync();
                    return (null, "Invalid file type");
                }

                const long MaxFileSize = 2 * 1024 * 1024;
                if (createEvent.EventPicture.Length > MaxFileSize)
                {
                    _context.Events.Remove(createObj);
                    await _context.SaveChangesAsync();
                    return (null, "File too large");
                }

                var uploadsFolder = Path.Combine(_enviroment.WebRootPath, "images", "events");
                Directory.CreateDirectory(uploadsFolder);
                var uniqueFileName = $"{Guid.NewGuid()}{fileExtension}";
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await createEvent.EventPicture.CopyToAsync(fileStream);
                }

                createObj.Img = $"/images/events/{uniqueFileName}";
            }

            await _context.SaveChangesAsync();

            var eventParticipantDtos = createObj.EventParticipants.Select(ep => new EventParticipantDto
            {
                UserName = UserName,
                Role = ep.Role,
            }).ToList();

            var evnt = new ReadEventDto()
            {
                EventId = createObj.Id,
                UserId = createObj.CreatedByUserId,
                Title = createObj.Title,
                Description = createObj.Description,
                StartTime = DateTime.SpecifyKind(createObj.StartTime, DateTimeKind.Utc).ToString("o"),
                EndTime = DateTime.SpecifyKind(createObj.EndTime, DateTimeKind.Utc).ToString("o"),
                Location = createObj.Location,
                AgeRangeMax = createObj.AgeRangeMax,
                AgeRangeMin = createObj.AgeRangeMin,
                Interests = interests.Select(i => i.Name).ToList(),
                EventParticipants = eventParticipantDtos,
                IsPublic = createObj.IsPublic,
                Img = createObj.Img
            };

            return (evnt, null);
        }

        public async Task<IEnumerable<ReadEventDto>> ReadAllPublicEvents(List<string?> interests, int? ageMin, int? ageMax, string sort, int page = 1, int pageSize = 10)
        {
            var query = _context.Events
                .AsNoTracking()
                .Include(e => e.EventParticipants)
                .ThenInclude(ep => ep.User)
                .Include(e => e.EventInterests)
                .ThenInclude(ei => ei.Interest)
                .Where(e => e.IsPublic && e.EndTime > DateTime.UtcNow);

            var interestIds = new List<int>();
            if (interests != null)
            {
                interestIds = await _context.Interests
                    .Where(i => interests.Contains(i.Name))
                    .Select(i => i.Id)
                    .ToListAsync();
            }

            if (ageMin != null && ageMax != null)
            {
                if (ageMin > ageMax) return new List<ReadEventDto>();

                query = query.Where(e => e.AgeRangeMax >= ageMin && e.AgeRangeMin <= ageMax);
            }
            else if (ageMin != null)
            {
                query = query.Where(e => e.AgeRangeMax >= ageMin);
            }
            else if (ageMax != null)
            {
                query = query.Where(e => e.AgeRangeMin >= ageMax);
            }

            if (interests != null && interests.Any(i => !string.IsNullOrEmpty(i)))
            {
                query = query.Where(e => e.EventInterests!.Any(i => interestIds.Contains(i.InterestId)));
            }

            if (sort == "alphabetical")
            {
                query = query.OrderBy(e => e.Title);
            }
            else 
            {
                query = query.OrderBy(e => e.StartTime);
            }

            query = query 
                .Skip((page - 1) * pageSize)
                .Take(pageSize);

            var result = await query.ToListAsync();

            var eventList = result.Select(r => new ReadEventDto
            {
                EventId = r.Id,
                UserId = r.CreatedByUserId,
                Title = r.Title,
                Description = r.Description,
                StartTime = DateTime.SpecifyKind(r.StartTime, DateTimeKind.Utc).ToString("o"),
                EndTime = DateTime.SpecifyKind(r.EndTime, DateTimeKind.Utc).ToString("o"),
                Location = r.Location,
                AgeRangeMax = r.AgeRangeMax,
                AgeRangeMin = r.AgeRangeMin,
                Img = r.Img,
                Interests = r.EventInterests?.Select(i => i.Interest != null ? i.Interest.Name : "").ToList(),
                EventParticipants = r.EventParticipants.Select(p => new EventParticipantDto
                {
                    UserName = p.User?.UserName,
                    Role = p.Role,
                }).ToList(),
                IsPublic = r.IsPublic
            }).ToList();

            return eventList;
        }

        public async Task<IEnumerable<ReadEventDto>> GetAllFriendEvents()
        {
            var friendIds = await _context.Friendships
                .Where(f => f.UserId == UserId)
                .Select(f => f.FriendId)
                .ToListAsync();

            var events = await _context.Events
                .Where(e => friendIds.Contains(e.CreatedByUserId) && e.EndTime > DateTime.UtcNow)
                .Include(e => e.EventParticipants)
                .ThenInclude(ep => ep.User)
                .Include(e => e.EventInterests)
                .ThenInclude(ei => ei.Interest)
                .ToListAsync();

            var eventList = events.Select(e => new ReadEventDto
            {
                EventId = e.Id,
                UserId = e.CreatedByUserId,
                Title = e.Title,
                Description = e.Description,
                StartTime = DateTime.SpecifyKind(e.StartTime, DateTimeKind.Utc).ToString("o"),
                EndTime = DateTime.SpecifyKind(e.EndTime, DateTimeKind.Utc).ToString("o"),
                Location = e.Location,
                AgeRangeMax = e.AgeRangeMax,
                AgeRangeMin = e.AgeRangeMin,
                Img = e.Img,
                Interests = e.EventInterests?.Select(i => i.Interest != null ? i.Interest.Name : "").ToList(),
                EventParticipants = e.EventParticipants.Select(p => new EventParticipantDto
                {
                    UserName = p.User?.UserName,
                    Role = p.Role,
                }).ToList(),
                IsPublic = e.IsPublic
            }).ToList();

            return eventList;
        }

        public async Task<IEnumerable<ReadEventDto>> GetUnjoinedEvents(List<string?> interests, int? ageMin, int? ageMax)
        {
            var query = _context.Events
                .Include(e => e.EventParticipants)
                    .ThenInclude(ep => ep.User)
                .Include(e => e.EventInterests)
                    .ThenInclude(ei => ei.Interest)
                .Where(e => e.IsPublic &&
                    !e.EventParticipants.Any(ep => ep.UserId == UserId));


            var interestIds = new List<int>();
            if (interests != null)
            {
                interestIds = await _context.Interests
                .Where(i => interests.Contains(i.Name))
                .Select(i => i.Id)
                .ToListAsync();
            }

            if (ageMin != null && ageMax != null)
            {
                if (ageMin > ageMax) return new List<ReadEventDto>();

                query = query.Where(e => e.AgeRangeMax >= ageMin && e.AgeRangeMin <= ageMax);
            }
            else if (ageMin != null)
            {
                query = query.Where(e => e.AgeRangeMax >= ageMin);
            }
            else if (ageMax != null)
            {
                query = query.Where(e => e.AgeRangeMin >= ageMax);
            }

            if (interests != null && interests.Any(i => !string.IsNullOrEmpty(i)))
            {
                query = query.Where(e => e.EventInterests!.Any(i => interestIds.Contains(i.EventId)));
            }

            var result = await query.ToListAsync();

            var eventList = result.Select(r => new ReadEventDto
            {
                EventId = r.Id,
                UserId = r.CreatedByUserId,
                Title = r.Title,
                Description = r.Description,
                StartTime = DateTime.SpecifyKind(r.StartTime, DateTimeKind.Utc).ToString("o"),
                EndTime = DateTime.SpecifyKind(r.EndTime, DateTimeKind.Utc).ToString("o"),
                Location = r.Location,
                AgeRangeMax = r.AgeRangeMax,
                AgeRangeMin = r.AgeRangeMin,
                Img = r.Img,
                Interests = r.EventInterests?.Select(i => i.Interest != null ? i.Interest.Name : "").ToList(),
                EventParticipants = r.EventParticipants.Select(p => new EventParticipantDto
                {
                    UserName = p.User?.UserName,
                    Role = p.Role,
                }).ToList(),
                IsPublic = r.IsPublic
            }).ToList();

            return eventList;
        }

        public async Task<ReadEventDto?> ReadEvent(int id)
        {
            var result = await _context.Events
                .Include(e => e.EventInterests)
                .ThenInclude(ei => ei.Interest)
                .Include(e => e.EventParticipants)
                .ThenInclude(ep => ep.User)
                .Include(e => e.Conversation)
                .ThenInclude(c => c.Messages)
                .ThenInclude(m => m.Sender)
                .FirstOrDefaultAsync(e => e.Id == id);
            if (result == null) return null;

            if (result.IsPublic)
            {
                var evnt = new ReadEventDto()
                {
                    EventId = result.Id,
                    UserId = result.CreatedByUserId,
                    Title = result.Title,
                    Description = result.Description,
                    StartTime = DateTime.SpecifyKind(result.StartTime, DateTimeKind.Utc).ToString("o"),
                    EndTime = DateTime.SpecifyKind(result.EndTime, DateTimeKind.Utc).ToString("o"),
                    Location = result.Location,
                    AgeRangeMax = result.AgeRangeMax,
                    AgeRangeMin = result.AgeRangeMin,
                    Img = result.Img,
                    Interests = result.EventInterests?.Select(i => i.Interest != null ? i.Interest.Name : "").ToList(),
                    EventParticipants = result.EventParticipants.Select(p => new EventParticipantDto
                    {
                        UserName = p.User?.UserName,
                        Role = p.Role,
                    }).ToList(),
                    IsPublic = result.IsPublic,
                    ConversationId = result.Conversation?.Id ?? 0,
                    EventMessages = result.Conversation?.Messages
                        .OrderBy(m => m.CreatedAt)
                        .Select(m => new EventMessageDto
                        {
                            Content = m.Content,
                            SenderId = m.SenderId,
                            CreatedAt = m.CreatedAt,
                            MessageId = m.Id,
                            SenderName = m.Sender?.UserName
                        }).ToList() ?? new List<EventMessageDto>()
                };

                return evnt;
            }
            else
            {
                var eventOwnerId = result.CreatedByUserId;
                var isFriend = await _context.Friendships.AnyAsync(f => 
                    (f.UserId == UserId && f.FriendId == eventOwnerId) ||
                    (f.FriendId == UserId && f.UserId == eventOwnerId)
                );

                if (!isFriend) return null;

                var evnt = new ReadEventDto()
                {
                    EventId = result.Id,
                    UserId = result.CreatedByUserId,
                    Title = result.Title,
                    Description = result.Description,
                    StartTime = DateTime.SpecifyKind(result.StartTime, DateTimeKind.Utc).ToString("o"),
                    EndTime = DateTime.SpecifyKind(result.EndTime, DateTimeKind.Utc).ToString("o"),
                    Location = result.Location,
                    AgeRangeMax = result.AgeRangeMax,
                    AgeRangeMin = result.AgeRangeMin,
                    Img = result.Img,
                    Interests = result.EventInterests?.Select(i => i.Interest != null ? i.Interest.Name : "").ToList(),
                    EventParticipants = result.EventParticipants.Select(p => new EventParticipantDto
                    {
                        UserName = UserName,
                        Role = p.Role,
                    }).ToList(),
                    IsPublic = result.IsPublic,
                    EventMessages = result.Conversation?.Messages
                        .OrderBy(m => m.CreatedAt)
                        .Select(m => new EventMessageDto
                        {
                            Content = m.Content,
                            SenderId = m.SenderId,
                            CreatedAt = m.CreatedAt,
                            MessageId = m.Id,
                            SenderName = m.Sender?.UserName
                        }).ToList() ?? new List<EventMessageDto>()
                };

                return evnt;
            }
        }

        public async Task<(bool, string)> UpdateEvent(int id, EventDto updateEvent)
        {
            var evnt = await _context.Events
                .Include(e => e.EventInterests)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (evnt == null)
                return (false, "Event not found");

            var interests = new List<Interest>();
            if (updateEvent.Interests != null)
            {
                interests = await _context.Interests
                    .Where(i => updateEvent.Interests.Contains(i.Name))
                    .ToListAsync();
            }

            evnt.EventInterests?.Clear();
            foreach (var interest in interests)
            {
                evnt.EventInterests!.Add(new EventInterest
                {
                    EventId = evnt.Id,
                    InterestId = interest.Id
                });
            }

            if (updateEvent.EventPicture != null)
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
                var fileExtension = Path.GetExtension(updateEvent.EventPicture.FileName).ToLowerInvariant();

                if (!allowedExtensions.Contains(fileExtension))
                {
                    return (false, "Invalid file type");
                }

                const long MaxFileSize = 2 * 1024 * 1024; 
                if (updateEvent.EventPicture.Length > MaxFileSize)
                {
                    return (false, "File too large");
                }

                var today = DateTime.UtcNow.Date;

                if (!evnt.LastImageUpdate.HasValue || evnt.LastImageUpdate.Value.Date < today)
                {
                    evnt.ImageUpdateCountToday = 0;
                }

                if (evnt.ImageUpdateCountToday >= 3)
                {
                    return (false, "You can only update the event image 3 times per day");
                }

                var uploadsFolder = Path.Combine(_enviroment.WebRootPath, "images", "events");
                Directory.CreateDirectory(uploadsFolder);

                var uniqueFileName = $"{Guid.NewGuid()}{fileExtension}";
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await updateEvent.EventPicture.CopyToAsync(fileStream);
                }

                if (!string.IsNullOrEmpty(evnt.Img))
                {
                    var oldFilePath = Path.Combine(_enviroment.WebRootPath, evnt.Img.TrimStart('/'));
                    if (File.Exists(oldFilePath))
                        File.Delete(oldFilePath);
                }

                evnt.Img = $"/images/events/{uniqueFileName}";
            }

            evnt.LastImageUpdate = DateTime.UtcNow;
            evnt.ImageUpdateCountToday += 1;
            evnt.Title = updateEvent.Title;
            evnt.Description = updateEvent.Description;
            evnt.Location = updateEvent.Location;
            evnt.AgeRangeMax = updateEvent.AgeRangeMax;
            evnt.AgeRangeMin = updateEvent.AgeRangeMin;
            evnt.IsPublic = updateEvent.IsPublic;
            evnt.StartTime = updateEvent.StartTime;
            evnt.EndTime = updateEvent.EndTime;

            _context.Update(evnt);
            await _context.SaveChangesAsync();

            return (true, "Event updated successfully");
        }

        public async Task<bool> JoinEvent(int id)
        {
            var result = await _context.Events.Include(e => e.EventParticipants).Include(e => e.Conversation).SingleOrDefaultAsync(e => e.Id == id);
            var user = await _context.Users.SingleOrDefaultAsync(u => u.Id == UserId);
            var canJoin = false;

            if (result == null || user == null || result.EventParticipants.Any(p => p.UserId == UserId)) return false;

            if (result.IsPublic)
            {
                var today = DateOnly.FromDateTime(DateTime.Today);
                var age = today.Year - user.DateOfBirth.Year;
                if (user.DateOfBirth > today.AddYears(-age)) age--;

                if (result.AgeRangeMax == null && result.AgeRangeMin == null) canJoin = true;
                else if (age >= result.AgeRangeMin && age <= result.AgeRangeMax) canJoin = true;
                else if (result.AgeRangeMin == null && age <= result.AgeRangeMax) canJoin = true;
                else if (result.AgeRangeMax == null && age >= result.AgeRangeMin) canJoin = true;
                else canJoin = false;
                
                if (canJoin)
                {
                    var participant = new EventParticipant()
                    {
                        UserId = UserId,
                        EventId = result.Id
                    };

                    var conversationParticipant = new ConversationParticipant()
                    {
                        UserId = UserId,
                        Role = "Participant",
                        ConversationId = result.ConversationId
                    };

                    result.EventParticipants.Add(participant);
                    result.Conversation?.ConversationParticipants.Add(conversationParticipant);
                    await _context.SaveChangesAsync();

                    return true;
                }

                return false;
            }
            else
            {
                var host = result.EventParticipants.SingleOrDefault(e => e.Role == "Host");
                if (host == null) return false;

                var friendship = await _context.Friendships.SingleOrDefaultAsync(f => (f.UserId == UserId && f.FriendId == host.UserId) || (f.UserId == host.UserId && f.FriendId == UserId));
                if (friendship == null) return false;

                var today = DateOnly.FromDateTime(DateTime.Today);
                var age = today.Year - user.DateOfBirth.Year;
                if (user.DateOfBirth > today.AddYears(-age)) age--;

                if (result.AgeRangeMax == null && result.AgeRangeMin == null) canJoin = true;
                else if (age >= result.AgeRangeMin && age <= result.AgeRangeMax) canJoin = true;
                else if (result.AgeRangeMin == null && age <= result.AgeRangeMax) canJoin = true;
                else if (result.AgeRangeMax == null && age >= result.AgeRangeMin) canJoin = true;
                else canJoin = false;

                if (canJoin)
                {
                    var participant = new EventParticipant()
                    {
                        UserId = UserId,
                        EventId = result.Id
                    };

                    var conversationParticipant = new ConversationParticipant()
                    {
                        UserId = UserId,
                        Role = "Participant",
                        ConversationId = result.ConversationId
                    };

                    result.EventParticipants.Add(participant);
                    result.Conversation?.ConversationParticipants.Add(conversationParticipant);
                    await _context.SaveChangesAsync();

                    return true;
                }

                return false;
            }
        }

        public async Task<bool> LeaveEvent(int id)
        {
            var result = await _context.Events.Include(e => e.EventParticipants).SingleOrDefaultAsync(e => e.Id == id);
            var user = await _context.Users.SingleOrDefaultAsync(u => u.Id == UserId);
            if (result == null || user == null) return false;

            var participant = result.EventParticipants.SingleOrDefault(ep => ep.UserId == UserId);

            if (participant != null)
            {
                result.EventParticipants.Remove(participant);
                var conversationParticipant = await _context.ConversationParticipants
                    .SingleOrDefaultAsync(cp => cp.ConversationId == result.ConversationId && cp.UserId == UserId);

                if (conversationParticipant != null)
                {
                    _context.ConversationParticipants.Remove(conversationParticipant);
                }
                else
                {
                    await _context.SaveChangesAsync();
                    return true;
                }

                await _context.SaveChangesAsync();
                return true;
            }

            return false;
        }

        public async Task<bool> DeleteEvent(int id)
        {
            var deleteEvent = await _context.Events.SingleOrDefaultAsync(e => e.Id == id);

            if (deleteEvent != null)
            {
                var eventParticipants = deleteEvent.EventParticipants;
                _context.EventParticipants.RemoveRange(eventParticipants);
                _context.Events.Remove(deleteEvent);
                await _context.SaveChangesAsync();
                return true;
            }

            return false;
        }

        public async Task<bool> HostDeleteEvent(int id)
        {
            var deleteEvent = await _context.Events
                .Include(e => e.EventParticipants)
                .Include(e => e.Conversation)
                .SingleOrDefaultAsync(e => e.Id == id && e.CreatedByUserId == UserId);

            if (deleteEvent != null)
            {
                if (deleteEvent.Conversation != null)
                    _context.Conversations.Remove(deleteEvent.Conversation);

                _context.EventParticipants.RemoveRange(deleteEvent.EventParticipants);

                if (!string.IsNullOrEmpty(deleteEvent.Img))
                {
                    var oldFilePath = Path.Combine(_enviroment.WebRootPath, deleteEvent.Img.TrimStart('/'));
                    if (File.Exists(oldFilePath))
                        File.Delete(oldFilePath);
                }

                _context.Events.Remove(deleteEvent);

                await _context.SaveChangesAsync();
                return true;
            }

            return false;
        }

        public async Task<List<string>> GetInterests()
        {
            return await _context.Interests
                .OrderBy(i => i.Name)
                .Select(i => i.Name)
                .ToListAsync();
        }

        public async Task<List<int>> EventPartcipantStatus()
        {
            var events = await _context.Events
                .Include(e => e.EventParticipants)
                .Where(e => e.EventParticipants.Any(ep => ep.UserId == UserId))
                .Select(e => e.Id)
                .ToListAsync();

            return events;
        }

        public async Task<IEnumerable<ReadEventDto>> GetMyCreatedEvents()
        {
            var events = await _context.Events
                .Where(e => e.CreatedByUserId == UserId)
                .Select(e => new ReadEventDto
                {
                    EventId = e.Id,
                    Title = e.Title,
                    Description = e.Description,
                    StartTime = DateTime.SpecifyKind(e.StartTime, DateTimeKind.Utc).ToString("o"),
                    EndTime = DateTime.SpecifyKind(e.EndTime, DateTimeKind.Utc).ToString("o"),
                    Location = e.Location,
                    UserId = e.CreatedByUserId,
                    Img = e.Img
                })
                .ToListAsync();

            return events;
        }

        public async Task<IEnumerable<ReadEventDto>> GetMyJoinedEvents()
        {
            var events = await _context.Events
                .Where(e => e.EventParticipants.Any(ep => ep.UserId == UserId) && e.CreatedByUserId != UserId)
                .Select(e => new ReadEventDto
                {
                    EventId = e.Id,
                    Title = e.Title,
                    Description = e.Description,
                    StartTime = DateTime.SpecifyKind(e.StartTime, DateTimeKind.Utc).ToString("o"),
                    EndTime = DateTime.SpecifyKind(e.EndTime, DateTimeKind.Utc).ToString("o"),
                    Location = e.Location,
                    UserId = e.CreatedByUserId,
                    Img = e.Img
                })
                .ToListAsync();

            return events;
        }

        //public async Task<bool> SendMessage(int id, string text)
        //{
        //    var evnt = await _context.Events
        //        .Include(e => e.EventParticipants)
        //        .FirstOrDefaultAsync(e => e.Id == id);

        //    if (evnt == null) return false;

        //    var isParticipant = evnt.EventParticipants.Any(ep => ep.UserId == UserId);
        //    if (!isParticipant) return false;

        //    var message = new Message()
        //    {
        //        Content = text,
        //        SenderId = UserId
        //    };

        //    if (message != null)
        //    {
        //        await _context.AddAsync(message);
        //        await _context.SaveChangesAsync();

        //        return true;
        //    }

        //    return false;
        //}

        //public async Task<IEnumerable<EventReceiveMessageDto>> GetEventMessages(int id)
        //{
        //    var evnt = await _context.Events
        //        .SingleOrDefaultAsync(e => e.Id == id);

        //    if (evnt == null)
        //        return new List<EventReceiveMessageDto>();

        //    var eventMessages = evnt.Messages
        //        .Select(m => new EventReceiveMessageDto
        //        {
        //            Content = m.Content,
        //            SenderId = m.SenderId,
        //            CreatedAt = m.CreatedAt,
        //        })
        //        .ToList();

        //    return eventMessages;
        //}
    }
}
