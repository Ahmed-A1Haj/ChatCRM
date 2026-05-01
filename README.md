# ChatCRM

> A modern, real-time WhatsApp chat management dashboard built on top of ASP.NET Core 10. Manage customer conversations from a single web interface — no phone switching, no tab chaos.

ChatCRM pairs a full authentication system with a WhatsApp-style dashboard so your team can reply to customers directly from the browser. Messages flow in real time via SignalR, conversations persist in SQL Server, and the WhatsApp link is handled by Evolution API (Baileys).

---

## Table of contents

1. [Features](#-features)
2. [Tech stack](#-tech-stack)
3. [Architecture](#-architecture)
4. [Screenshots](#-screenshots)
5. [Prerequisites](#-prerequisites)
6. [Quick start (mock mode — 2 min)](#-quick-start-mock-mode--2-minutes)
7. [Production setup (real WhatsApp)](#-production-setup-real-whatsapp)
8. [Database schema](#-database-schema)
9. [Routes & endpoints](#-routes--endpoints)
10. [Project structure](#-project-structure)
11. [Configuration reference](#-configuration-reference)
12. [Troubleshooting](#-troubleshooting)
13. [Security & legal](#-security--legal)

---

## ✨ Features

### Authentication & accounts
- Email-based registration with verification
- Secure login with lockout after 5 failed attempts
- Password reset via email
- Profile management (name, phone, avatar upload)
- Email change with re-verification

### WhatsApp dashboard
- **Conversation list** with unread badges, last-message preview, and relative timestamps
- **Chat window** with WhatsApp-style incoming/outgoing bubbles
- **Real-time updates** — new messages appear instantly via SignalR
- **Send replies** — type + enter, messages go out via Evolution API
- **Auto-scroll** to latest message + date separators
- **Search** conversations by name or phone
- **Mock mode** for development without a real WhatsApp connection

### Developer-friendly
- Clean Architecture (Domain → Application → Infrastructure → MVC)
- Swappable Evolution backend via one config flag (`UseMock`)
- Auto-apply migrations on startup
- Seeded demo data + simulated inbound messages in mock mode

---

## 🧱 Tech stack

| Layer            | Technology                                      |
| ---------------- | ----------------------------------------------- |
| Runtime          | .NET 10 / ASP.NET Core 10 (MVC)                 |
| Language         | C# (nullable reference types enabled)           |
| Database         | SQL Server LocalDB (dev) / any SQL Server       |
| ORM              | Entity Framework Core 10                        |
| Authentication   | ASP.NET Core Identity                           |
| Real-time        | SignalR (WebSocket)                             |
| Validation       | FluentValidation                                |
| WhatsApp bridge  | [Evolution API](https://github.com/EvolutionAPI/evolution-api) (Baileys) |
| Frontend         | Razor Views + vanilla JS + Bootstrap 5          |

---

## 🏛 Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                     Browser  (Razor + SignalR client)           │
└───────────────────────┬─────────────────────────────────────────┘
                        │  HTTPS  +  WebSocket /hubs/chat
                        ▼
┌─────────────────────────────────────────────────────────────────┐
│                    ChatCRM.MVC  (ASP.NET Core)                  │
│                                                                 │
│  Controllers:                                                   │
│   • AccountController       /Account/...                        │
│   • DashboardController     /dashboard/chats                    │
│   • WebhookController       /api/evolution/webhook  (public)    │
│                                                                 │
│  SignalR ChatHub             /hubs/chat                         │
└───────────────────────┬─────────────────────────────────────────┘
                        │
        ┌───────────────┼────────────────┐
        ▼               ▼                ▼
┌───────────────┐ ┌─────────────┐ ┌───────────────────┐
│ IChatService  │ │ IEvolution  │ │  AppDbContext     │
│ ChatService   │ │ Service     │ │  (EF Core)        │
│               │ │ (real/mock) │ │                   │
└───────────────┘ └──────┬──────┘ └─────────┬─────────┘
                         │                  │
                         │ HTTPS            │ SQL
                         ▼                  ▼
                 ┌──────────────┐   ┌──────────────┐
                 │ Evolution API│   │ SQL Server   │
                 │ + Baileys    │   │   LocalDB    │
                 └──────┬───────┘   └──────────────┘
                        │
                        ▼
                 ┌──────────────┐
                 │  WhatsApp    │
                 └──────────────┘
```

**Message flow (inbound):**
`Customer's phone → WhatsApp → Baileys → Evolution API → webhook → WebhookController → EvolutionService.HandleIncomingWebhookAsync → save to DB + broadcast via SignalR → browser updates`

**Message flow (outbound):**
`Agent types reply → chat.js POST /dashboard/chats/send → ChatService.SendMessageAsync → save to DB + call Evolution API → Baileys → WhatsApp → customer's phone`

---

## 📸 Screenshots

**Dashboard — empty state + seeded conversations**

```
┌──────────────────────────────────────────────────────────────────┐
│  ChatCRM    Home  💬 Chats  Privacy              👤 Majd  Sign out│
├──────────────┬───────────────────────────────────────────────────┤
│Conversations │                                                   │
│              │                                                   │
│🔍 Search…    │         ╭─────────────────────╮                   │
│──────────────│         │   💬 Select a chat   │                   │
│👤 Alice     ●│         │   Click any on left   │                  │
│  Hey, thanks!│         ╰─────────────────────╯                   │
│  2m     [ 1 ]│                                                   │
│──────────────│                                                   │
│👤 Bob        │                                                   │
│  Thanks!     │                                                   │
│  45m         │                                                   │
│──────────────│                                                   │
│👤 Carol     ●│                                                   │
│  And logos?  │                                                   │
│  now    [ 3 ]│                                                   │
│──────────────│                                                   │
│              │                                                   │
└──────────────┴───────────────────────────────────────────────────┘
```

---

## 📋 Prerequisites

- **.NET 10 SDK** — https://dot.net/download
- **SQL Server LocalDB** (ships with Visual Studio, or download SSDT)
- Optional for real WhatsApp: an **Evolution API** instance (see [Production setup](#-production-setup-real-whatsapp))

---

## 🚀 Quick start (mock mode — 2 minutes)

Mock mode uses an in-process fake WhatsApp backend — 3 seeded conversations and an inbound message simulator that fires every 45 seconds. Perfect for UI work or trying the dashboard without linking a real phone.

### 1. Clone and restore
```bash
git clone <your-repo-url> ChatCRM
cd ChatCRM
dotnet restore
```

### 2. Create your local dev secrets
Create `ChatCRM.MVC/appsettings.Development.json`:
```json
{
  "Smtp": {
    "Username": "your-gmail@gmail.com",
    "Password": "your-gmail-app-password"
  },
  "Evolution": {
    "UseMock": true
  }
}
```
> 💡 Gmail requires a **Google App Password**, not your normal password. Generate one at https://myaccount.google.com/apppasswords

### 3. Run it
```bash
dotnet run --project ChatCRM.MVC
```

### 4. Open the app
- Go to **https://localhost:7224**
- Click **Create account** → register → confirm email → log in
- Click **💬 Chats** in the navbar

You'll see 3 seeded conversations. Every 45 seconds, a random one gets a new inbound message — the sidebar reorders, the badge ticks up, and if the chat is open, the new bubble drops in with a subtle animation.

---

## 🌐 Production setup (real WhatsApp)

Connecting to real WhatsApp requires an **Evolution API** instance. Three ways to run one:

### Option A — Railway (recommended, ~$5/month)

Railway's one-click template deploys Evolution API + PostgreSQL + Redis with SSL, ready in ~60 seconds.

1. Sign up at **https://railway.com** with GitHub
2. Deploy the **[Evolution API template](https://railway.com/deploy/evolution-api-whatsapp-automation)**
3. In the deployed service's **Variables** tab, add:
   ```
   CONFIG_SESSION_PHONE_VERSION = 2.3000.1023204200
   ```
   > ⚠️ This is critical. Without it, Baileys connects but WhatsApp rejects the QR scan with "couldn't connect to device".
4. Wait for the service to redeploy
5. Note the **public URL** (Settings → Networking) and the **AUTHENTICATION_API_KEY** (Variables tab)

### Option B — Self-host via Docker

A reference `docker-compose.yml` is included at `docker/docker-compose.yml`. Copy the example env file and fill in your own values first:
```bash
cd docker
cp .env.example .env
# edit .env and set AUTHENTICATION_API_KEY + POSTGRES_PASSWORD to long random strings
docker compose up -d
```
Evolution API becomes available at `http://localhost:8081`. Use the `AUTHENTICATION_API_KEY` you set in `.env` as the `apikey` header for all client requests.

> ⚠️ **Known caveat**: WhatsApp frequently rejects Baileys device links from home/residential IPs — especially Docker-on-WSL2 on Windows. Cloud hosting (Option A) has a much higher success rate.

### Step 2 — Link your WhatsApp number

Replace `$URL` and `$KEY` with your Evolution API details.

**Create the instance:**
```bash
curl -X POST "$URL/instance/create" \
  -H "apikey: $KEY" \
  -H "Content-Type: application/json" \
  -d '{"instanceName":"chatcrm","qrcode":true,"integration":"WHATSAPP-BAILEYS"}'
```

**Fetch a fresh QR:**
```bash
curl "$URL/instance/connect/chatcrm" -H "apikey: $KEY"
```

The response contains a `base64` PNG. Decode it to a file and scan:
```bash
node -e "const fs=require('fs'),d=JSON.parse(fs.readFileSync('qr.json','utf8'));fs.writeFileSync('qr.png',Buffer.from(d.base64.split(',')[1],'base64'))"
```

On your phone: **WhatsApp → ⋮ / Settings → Linked Devices → Link a Device** → scan the QR within 60 seconds.

### Step 3 — Expose your local app with ngrok

Evolution API needs to reach your local app's webhook. Install ngrok and create a free account at https://ngrok.com/signup, grab your authtoken, then:

```bash
ngrok config add-authtoken YOUR_AUTHTOKEN
ngrok http 5128
```

Copy the `https://*.ngrok-free.dev` URL it prints.

### Step 4 — Register the webhook

Pick any strong secret string (e.g. `openssl rand -hex 16`). Then:

```bash
curl -X POST "$URL/webhook/set/chatcrm" \
  -H "apikey: $KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "webhook": {
      "enabled": true,
      "url": "https://YOUR-NGROK-URL.ngrok-free.dev/api/evolution/webhook",
      "webhookByEvents": false,
      "events": ["MESSAGES_UPSERT"],
      "headers": { "x-webhook-secret": "YOUR_SECRET" }
    }
  }'
```

### Step 5 — Wire the credentials into your app

Update `ChatCRM.MVC/appsettings.Development.json`:
```json
{
  "Evolution": {
    "UseMock": false,
    "BaseUrl": "https://your-evolution-url.example.com",
    "ApiKey": "your-authentication-api-key",
    "InstanceName": "chatcrm",
    "WebhookSecret": "the-same-secret-from-step-4"
  }
}
```

### Step 6 — Restart and test

```bash
dotnet run --project ChatCRM.MVC
```

Send a WhatsApp message to your linked number from any phone. Within ~1 second, the message appears in the dashboard in real time. 🎉

> ⚠️ On ngrok's free plan, the tunnel URL changes every time you restart ngrok. Re-register the webhook (Step 4) after each restart, or pay for a reserved domain.

---

## 🗄️ Database schema

Auto-applied at startup via `dbContext.Database.Migrate()`.

```
┌─────────────────────────┐       ┌─────────────────────────┐
│   WhatsAppContacts      │       │     AspNetUsers         │
├─────────────────────────┤       ├─────────────────────────┤
│ Id          PK          │       │ Id          PK          │
│ PhoneNumber UNIQUE      │       │ FirstName               │
│ DisplayName             │       │ LastName                │
│ AvatarUrl               │       │ ProfileImagePath        │
│ CreatedAt               │       │ + Identity fields       │
└──────────┬──────────────┘       └───────────┬─────────────┘
           │ 1:N                              │ 1:N (nullable)
           ▼                                  ▼
┌─────────────────────────────────────────────────────────────┐
│                     Conversations                           │
├─────────────────────────────────────────────────────────────┤
│ Id                PK                                        │
│ ContactId         FK → WhatsAppContacts                     │
│ AssignedUserId    FK → AspNetUsers (nullable)               │
│ LastMessageAt     indexed                                   │
│ UnreadCount       denormalized counter                      │
│ IsArchived                                                  │
│ CreatedAt                                                   │
└──────────┬──────────────────────────────────────────────────┘
           │ 1:N
           ▼
┌─────────────────────────────────────────────────────────────┐
│                       Messages                              │
├─────────────────────────────────────────────────────────────┤
│ Id              PK                                          │
│ ConversationId  FK → Conversations, indexed                 │
│ Body            the message text                            │
│ Direction       0=Incoming, 1=Outgoing                      │
│ Status          0=Sent, 1=Delivered, 2=Read                 │
│ ExternalId      Evolution msg ID, UNIQUE (dedupes webhooks) │
│ SentAt          indexed                                     │
└─────────────────────────────────────────────────────────────┘
```

**Key design decisions:**
- `ExternalId` on `Messages` is unique with a filtered index — prevents duplicate webhook deliveries from creating dupe messages.
- `UnreadCount` is denormalized on `Conversation` so the sidebar can render without running `COUNT(*)` per row.
- `AssignedUserId` is ready for future multi-agent assignment but not used yet.

---

## 🛣️ Routes & endpoints

| Method | Route                                | Auth | Purpose                                    |
| ------ | ------------------------------------ | ---- | ------------------------------------------ |
| GET    | `/`                                  | —    | Home                                       |
| GET    | `/Account/Register`                  | —    | Registration form                          |
| POST   | `/Account/Register`                  | —    | Create account                             |
| GET    | `/Account/Login`                     | —    | Login form                                 |
| POST   | `/Account/Login`                     | —    | Authenticate                               |
| GET    | `/Account/ConfirmEmail`              | —    | Email verification callback                |
| GET    | `/Account/ForgotPassword`            | —    | Reset request                              |
| GET    | `/Account/ResetPassword`             | —    | Reset form                                 |
| GET/POST | `/Account/Profile`                 | ✅   | View / edit profile                        |
| POST   | `/Account/Logout`                    | ✅   | Sign out                                   |
| GET    | `/dashboard/chats`                   | ✅   | **Main dashboard**                         |
| GET    | `/dashboard/chats/{id}/messages`     | ✅   | Fetch messages for one conversation (JSON) |
| POST   | `/dashboard/chats/send`              | ✅   | Send a reply                               |
| POST   | `/api/evolution/webhook`             | 🔒*  | Evolution API → ChatCRM webhook receiver   |
| WS     | `/hubs/chat`                         | ✅   | SignalR hub                                |

🔒* = secured by `x-webhook-secret` header match against `Evolution:WebhookSecret` config.

---

## 📁 Project structure

```
ChatCRM/
├── ChatCRM.Domain/                 Pure entities, no framework deps
│   └── Entities/
│       ├── User.cs
│       ├── WhatsAppContact.cs
│       ├── Conversation.cs
│       └── Message.cs              incl. MessageDirection + MessageStatus enums
│
├── ChatCRM.Application/            DTOs, interfaces, validators
│   ├── Interfaces/
│   │   ├── IAppDbContext.cs
│   │   ├── IChatService.cs
│   │   └── IEvolutionService.cs
│   ├── Users/DTOS/                 Login, Register, ResetPassword, etc.
│   └── Chats/DTOs/                 Conversation, Message, SendMessage, WebhookPayload
│
├── ChatCRM.Persistence/            EF Core
│   ├── AppDbContext.cs
│   └── Migrations/
│       ├── 20260407... InitialMigrate
│       ├── 20260413... UserMigration + Config + fields
│       ├── 20260418... SwitchToIdentitySchema + profile fields
│       └── 20260420... AddWhatsAppModule      ← adds 3 new tables
│
├── ChatCRM.Infrastructure/         External-facing services
│   ├── Hubs/
│   │   └── ChatHub.cs              SignalR hub
│   └── Services/
│       ├── EvolutionService.cs     real Evolution API client
│       ├── ChatService.cs          send / fetch / mark-read business logic
│       ├── MockEvolutionService.cs dev-only no-op
│       ├── DemoDataSeeder.cs       seeds 3 contacts + messages in mock mode
│       ├── FakeMessageSimulator.cs fires inbound message every 45s in mock mode
│       └── EvolutionOptions.cs     strongly-typed config
│
├── ChatCRM.MVC/                    ASP.NET Core web app (entry point)
│   ├── Controllers/
│   │   ├── HomeController.cs
│   │   ├── AccountController.cs
│   │   ├── DashboardController.cs  (NEW)
│   │   └── WebhookController.cs    (NEW)
│   ├── Views/
│   │   ├── Account/                login, register, profile, reset, verify
│   │   ├── Dashboard/
│   │   │   └── Chats.cshtml        (NEW) full dashboard layout
│   │   └── Shared/_Layout.cshtml   top nav incl. 💬 Chats link
│   ├── Services/                   email + profile image services
│   ├── wwwroot/
│   │   ├── css/chat.css            (NEW) WhatsApp-Web styling
│   │   └── js/chat.js              (NEW) SignalR client + send logic
│   ├── Program.cs                  DI + middleware + DB migrate + seeder
│   ├── appsettings.json            committed — no secrets!
│   └── appsettings.Development.json gitignored — put real secrets here
│
├── ChatCRM.Common/                 (reserved for future cross-cutting utilities)
│
├── docker/
│   └── docker-compose.yml          self-hosted Evolution API + Postgres + Redis
│
└── README.md
```

---

## ⚙️ Configuration reference

### Settings layering

Configuration is loaded in this order (later overrides earlier):
1. `appsettings.json` — committed defaults
2. `appsettings.Development.json` — **gitignored**, put real secrets here
3. Environment variables — for production / CI

### Required keys

| Key                            | Purpose                                                    |
| ------------------------------ | ---------------------------------------------------------- |
| `ConnectionStrings:DefaultConnection` | EF Core connection string                           |
| `Smtp:Host` / `Port` / `EnableSsl` / `FromEmail` / `FromName` | Outgoing email         |
| `Smtp:Username` / `Password`   | Dev-only — put in `appsettings.Development.json`           |
| `Evolution:UseMock`            | `true` = no real WhatsApp; `false` = use Evolution API     |
| `Evolution:BaseUrl`            | Evolution API base URL (no trailing slash)                 |
| `Evolution:ApiKey`             | `AUTHENTICATION_API_KEY` from Evolution instance           |
| `Evolution:InstanceName`       | Your instance name, e.g. `chatcrm`                         |
| `Evolution:WebhookSecret`      | Any strong string — must match what you register with Evolution |

---

## 🔧 Troubleshooting

**Webhook arrives but message doesn't show in dashboard**
- Check browser DevTools console for SignalR errors.
- Check if the conversation is brand new — `chat.js` auto-reloads the page on first message from a new contact.
- Inspect ngrok's web UI at **http://127.0.0.1:4040** — every webhook is logged there with the full request/response.

**Webhook returns `307 Temporary Redirect`**
The app is forcing HTTPS, but ngrok forwards HTTP. Make sure `Program.cs` still branches `UseHttpsRedirection` to skip `/api/evolution/*`:
```csharp
app.UseWhen(
    ctx => !ctx.Request.Path.StartsWithSegments("/api/evolution"),
    branch => branch.UseHttpsRedirection());
```

**QR scan fails with "couldn't connect to device"**
Your Evolution API's Baileys version is rejected by WhatsApp. Set this env var on the Evolution host and restart:
```
CONFIG_SESSION_PHONE_VERSION = 2.3000.1023204200
```

**QR code keeps refreshing but can't scan fast enough**
Scan within 30–60 seconds. If it expires, hit `GET /instance/connect/chatcrm` again for a fresh one.

**`ngrok-agent version too old`**
```
ngrok update
```
Free accounts require agent v3.20+.

**Database errors on first run**
The app auto-runs `dotnet ef database update` on startup. If that fails, check:
- SQL Server LocalDB is installed and running
- The connection string in `appsettings.json` points to a reachable server
- The user running the app has DB-create permissions

---

## 🔒 Security & legal

### Secrets management
- **Never commit real Evolution API keys, webhook secrets, or SMTP passwords.**
- `appsettings.Development.json` is in `.gitignore` for exactly this reason.
- For production use [.NET User Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) or environment variables.

### WhatsApp terms of service
Evolution API uses the **unofficial WhatsApp Web protocol** (Baileys). This technically **violates WhatsApp's Terms of Service**. Risks:
- Your linked phone number may be banned without warning.
- Meta can (and does) break the protocol, causing silent downtime.

**For any real business use:** apply for the official **[Meta WhatsApp Cloud API](https://developers.facebook.com/docs/whatsapp/cloud-api)** via Facebook Business Manager. It's free for the first 1,000 conversations per month and fully ToS-compliant.

### Built-in security features
- ✅ ASP.NET Identity password hashing (PBKDF2)
- ✅ CSRF protection (`[ValidateAntiForgeryToken]`)
- ✅ HttpOnly + SameSite cookies
- ✅ Account lockout after 5 failed attempts
- ✅ Email verification required for login
- ✅ Webhook endpoint authenticated via shared secret
- ✅ Text sanitization on user input
- ✅ Path-traversal protection on profile image uploads

---

## 📄 License

Private / unpublished.

## 🙌 Credits

- [Evolution API](https://github.com/EvolutionAPI/evolution-api) — WhatsApp integration layer
- [Baileys](https://github.com/WhiskeySockets/Baileys) — underlying WhatsApp Web client
- [ASP.NET Core](https://dotnet.microsoft.com) — framework
- [SignalR](https://dotnet.microsoft.com/apps/aspnet/signalr) — real-time messaging
