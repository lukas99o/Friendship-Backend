# 🧩 Friendship — Backend API (.NET)

**Friendship API** powers the backend of the Friendship social app — enabling events, friendships, chat, profiles, and authentication.  
Built with **ASP.NET Core**, **Entity Framework Core**, and **Microsoft SQL**, fully hosted on **Azure**.

🔗 **Live API:** [https://friendship-c3cfdgejf5ateyc2.swedencentral-01.azurewebsites.net](https://friendship-c3cfdgejf5ateyc2.swedencentral-01.azurewebsites.net)
🌐 **Frontend:** [https://ashy-stone-09b187203.2.azurestaticapps.net/](https://ashy-stone-09b187203.2.azurestaticapps.net/)

---

## 🧭 About

The API provides secure and scalable endpoints for all features in the Friendship platform.  
It supports user authentication, real-time communication, and persistent data management.

Key functionality:
- 🔐 Authentication & Authorization (Identity + JWT)  
- 👥 Friendships & user relationships  
- 🎉 Event creation and participation  
- 💬 Real-time chat via SignalR  
- 🖼️ Profile picture upload & management  

---

## 🧠 Tech Stack

- **.NET 8 (ASP.NET Core Web API)**  
- **Entity Framework Core** with **Microsoft SQL Server**  
- **SignalR** for real-time communication  
- **Azure App Service** & **Azure SQL Database**  
- **JWT** for authentication  
- **Dependency Injection** and **Repository Pattern**

---

## 🚀 Deployement 
- API: Azure App Service
- Database: Azure SQL

---

## ⚙️ Setup

### 1️⃣ Clone & Navigate
```bash
git clone <repository-url>
cd friendship-backend
```

### 2️⃣ Configure Enviroment Variables
Create a file named .env and put these values: 
- **ConnectionStrings__DefaultConnection=[your connection string]**
- **JwtKey=[YourSuperDuperUltraSecretKey]**
- **JwtIssuer=https://localhost:7106**
- **JwtAudience=FriendshipAppAudience**
- **BaseUrl=http://localhost:7106**

### 3️⃣ Run Migrations
Open package managaer console and run:
```
update-database
```
## 📡 API Overview
- **AuthController** - register, login, JWT handling
- **FriendController** - manage friendships and send friendrequests
- **EventController** - create & join events
- **UserController** - profile & picture management

---

## ✨ Related Project
Frontend: 🔗 [Friendship React App](https://github.com/lukas99o/Friendship-App)
