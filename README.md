# ChatCRM

> A team-grade WhatsApp CRM built on ASP.NET Core 10. One inbox for every conversation, with billing, AI agents, contact management, templates, role-based access, and four-language UI — all in one self-hostable web app.

ChatCRM lets a team reply to WhatsApp customers from a single browser interface — no phone-swapping, no tab chaos. Messages stream in real time via SignalR, conversations persist in SQL Server, the WhatsApp link runs through Evolution API (Baileys) or Meta Cloud API, AI agents can auto-reply via a sidecar service, and every outgoing message is metered against a per-workspace Stripe-funded wallet.

---

## Table of contents

1. [Features](#-features)
2. [Tech stack](#-tech-stack)
3. [Architecture](#-architecture)
4. [Roles & permissions](#-roles--permissions)
5. [Localization](#-localization)
6. [Prerequisites](#-prerequisites)
7. [Quick start (mock mode — 2 min)](#-quick-start-mock-mode--2-minutes)
8. [Production setup (real WhatsApp)](#-production-setup-real-whatsapp)
9. [Stripe top-up (development testing)](#-stripe-top-up-development-testing)
10. [AI agents (optional)](#-ai-agents-optional)
11. [Database schema](#-database-schema)
12. [Routes & endpoints](#-routes--endpoints)
13. [Project structure](#-project-structure)
14. [Configuration reference](#-configuration-reference)
15. [Tests](#-tests)
16. [Troubleshooting](#-troubleshooting)
17. [Security & legal](#-security--legal)

---

## ✨ Features

### Authentication & accounts
- Email registration with verification
- Login with lockout after 5 failed attempts
- Password reset by email
- Profile management (name, phone, avatar upload, email change with re-verification)

### Real-time chat dashboard
- Conversation list with unread badges, last-message preview, relative timestamps
- WhatsApp-style chat window with incoming/outgoing bubbles and date separators
- Real-time message delivery via SignalR (no refresh)
- Send replies with Enter; outbound goes through Evolution API
- Internal **notes** that stay private to the team (never sent to WhatsApp)
- Message media: text, image, video, audio, document, sticker
- Edit + delete tracking (mirrors WhatsApp's edit-for-everyone and revoke)
- 24-hour customer-service window tracking for Cloud-API instances
- Per-conversation tags, lifecycle stage, status (Open / Snoozed / Closed) and assignment

### Contacts
- Searchable contact table with country, language, lifecycle stage, blocked status, tags
- Click-to-edit fields and lifecycle/agent pickers without leaving the row
- **Excel import** with downloadable template, header-name parsing (column order doesn't matter), per-row validation, duplicate detection (by phone), preview before commit, and downloadable error report for failed rows
- CSV export of the current filter
- Lead-card details modal works for both messaged and import-only contacts

### Multi-instance WhatsApp
- Connect multiple WhatsApp numbers (Personal/Baileys via QR or Business/Cloud API)
- Per-instance status, QR refresh, webhook registration
- Conversations are scoped to their originating instance

### AI agents
- Workspace-scoped agents with name, instructions, avatar, default/active flags
- Per-contact or per-conversation agent assignment (falls back to workspace default)
- Outbound reply queue (`AiOutboxMessage`) → background dispatcher → Evolution
- Sync to an external CRM-AI-Service over HTTP + Redis pub/sub for low-latency replies

### Billing & wallet
- Per-workspace wallet with optimistic concurrency
- **Pre-send billing gate** — outgoing messages refuse to ship if the wallet can't cover the Meta cost
- **Stripe Checkout** top-ups (test cards work end-to-end)
- Auto-recharge worker tops the wallet back up when it drops below a threshold
- Immutable audit log of every billing action (top-up, refund, manual adjustment)
- Generated **invoices** as PDF (QuestPDF) downloadable from the dashboard
- Stripe webhook deduplication so retried events don't double-credit

### Templates
- Local template library (Draft / Submitted / Approved / Rejected / Paused / Disabled / Stuck)
- One-click submission to Meta for approval, with placeholders + sample values
- Adaptive background poller mirrors Meta's status (and flags templates Stuck after 7 days)

### User & role management
- Role-based access control with 19 granular permissions across 8 groups
- Three seeded roles (**Admin**, **Manager**, **Agent**); workspace admins can edit role permissions
- Cross-workspace **Platform Admin** role gated behind a config-driven email allowlist

### Cross-workspace ops (Platform Admin)
- Platform dashboard: revenue, top workspaces, message volume by date range
- Audit-log viewer with action/entity-type/date filters

### Multi-language UI
- English, Russian, Romanian, Turkish out of the box
- JSON-backed localizer (not RESX) — drop a `strings.<culture>.json` file to add a language
- Language picker, cookie persistence, `Accept-Language` fallback

### Developer-friendly
- Clean Architecture (Domain → Application → Infrastructure → MVC), with a Tests project
- Swappable Evolution backend via one config flag (`UseMock`)
- Auto-apply migrations on startup, plus role/billing/instance/demo data seeders
- 27 xUnit tests covering the contact-import parser + validator

---

## 🧱 Tech stack

| Layer            | Technology                                                              |
| ---------------- | ----------------------------------------------------------------------- |
| Runtime          | .NET 10 / ASP.NET Core 10 (MVC)                                         |
| Language         | C# (nullable reference types enabled)                                   |
| Database         | SQL Server LocalDB (dev) / any SQL Server                               |
| ORM              | Entity Framework Core 10                                                |
| Authentication   | ASP.NET Core Identity + custom RBAC (claim-based `Permission` handler)  |
| Real-time        | SignalR (WebSocket)                                                     |
| Validation       | FluentValidation                                                        |
| WhatsApp bridge  | [Evolution API](https://github.com/EvolutionAPI/evolution-api) (Baileys) or Meta WhatsApp Cloud API |
| Payments         | [Stripe.NET](https://github.com/stripe/stripe-dotnet) (Checkout + webhooks)         |
| PDF generation   | [QuestPDF](https://www.questpdf.com) (invoices)                          |
| Excel I/O        | [ClosedXML](https://github.com/ClosedXML/ClosedXML) (contact import / error reports) |
| AI sidecar       | HTTP + Redis pub/sub to an external CRM-AI-Service (optional)            |
| Localization     | Custom JSON-file localizer (`strings.<culture>.json`)                    |
| Testing          | xUnit                                                                    |
| Frontend         | Razor Views + vanilla JS + custom CSS (no SPA framework)                |

---

## 🏛 Architecture

```
┌──────────────────────────────────────────────────────────────────────────┐
│                Browser (Razor + SignalR client + vanilla JS)             │
└──────────────────────────┬───────────────────────────────────────────────┘
                           │  HTTPS  +  WebSocket /hubs/chat
                           ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                       ChatCRM.MVC  (ASP.NET Core)                        │
│                                                                          │
│  Controllers (selected):                                                 │
│   • AccountController        /Account/...                                │
│   • DashboardController      /dashboard/chats, /dashboard/...            │
│   • ContactsController       /dashboard/contacts + /api/contacts/*       │
│   • BillingController        /dashboard/billing*                         │
│   • InvoicesController       /dashboard/billing/invoices*                │
│   • TemplatesController      /dashboard/templates*                       │
│   • AgentsController         /dashboard/agents*                          │
│   • UsersController          /dashboard/settings/users*                  │
│   • RolesController          /dashboard/settings/roles*                  │
│   • InstancesController      /api/instances*                             │
│   • AuditController          /dashboard/audit*       (Platform Admin)    │
│   • PlatformAdminController  /dashboard/platform-admin* (Platform Admin) │
│   • WebhookController        /api/evolution/webhook  (public secret)     │
│   • StripeWebhookController  /api/webhooks/stripe    (Stripe-signed)     │
│                                                                          │
│  SignalR  ChatHub  /hubs/chat                                            │
│  RBAC: PermissionAuthorizationHandler resolves claim-typed Permission    │
└──────────────────────────┬───────────────────────────────────────────────┘
                           │
   ┌──────────┬────────────┼──────────────┬──────────────┐
   ▼          ▼            ▼              ▼              ▼
┌──────┐ ┌──────────┐ ┌──────────┐ ┌──────────────┐ ┌────────────┐
│ Chat │ │ Billing  │ │ Contacts │ │ Templates    │ │  Agents    │
│ Svc  │ │ + Wallet │ │ + Import │ │ + Meta sync  │ │  + AI loop │
└──┬───┘ └────┬─────┘ └────┬─────┘ └──────┬───────┘ └──────┬─────┘
   │         │             │              │                │
   │         ▼             │              ▼                ▼
   │   ┌──────────┐        │       ┌──────────────┐ ┌────────────────┐
   │   │  Stripe  │        │       │  Meta Graph  │ │ CRM-AI-Service │
   │   │ Checkout │        │       │   API (WA    │ │ (HTTP + Redis  │
   │   │ + hooks  │        │       │   templates) │ │   pub/sub)     │
   │   └──────────┘        │       └──────────────┘ └────────────────┘
   ▼                       ▼
┌──────────────────────────────────────────────────────────────────────────┐
│         AppDbContext (EF Core) ───►  SQL Server (LocalDB / Azure)        │
└──────────────────────────┬───────────────────────────────────────────────┘
                           │
                           ▼
                  ┌────────────────────┐
                  │  Evolution API     │── HTTPS →  WhatsApp
                  │  (Baileys/Cloud)   │
                  └────────────────────┘
```

**Inbound message flow:**
`Customer's phone → WhatsApp → Baileys → Evolution API → webhook → WebhookController → ChatService → save + (optional) AI agent dispatcher → broadcast via SignalR → browser updates`

**Outbound message flow:**
`Agent types reply → chat.js POST /dashboard/chats/send → ChatService.SendMessageAsync → BillingGate (cost check vs wallet) → save to DB → call Evolution API → Baileys → WhatsApp → customer's phone`

**AI auto-reply flow:**
`Incoming message → AiReplyDispatcher → enqueue AiOutboxMessage → AiOutboundConsumer (background) → CRM-AI-Service → reply text → AiOutboxPublisher → ChatService.SendMessageAsync`

---

## 🔐 Roles & permissions

Authorization is **claim-based RBAC**. Roles hold `Permission` claims (e.g. `contacts.edit`, `billing.topup`), and a custom `PermissionAuthorizationHandler` checks the user's combined claim set against `[RequirePermission(...)]` on each action.

### Seeded roles

| Role        | Permissions                                                                            |
| ----------- | -------------------------------------------------------------------------------------- |
| **Admin**   | Everything in the workspace — full CRUD on users, roles, billing refunds, channels, templates, agents. (Does **not** include `platform.admin`.) |
| **Manager** | Operations lead — billing top-ups, users & roles, contacts, conversations, channels, templates view, agents view. No refund power. |
| **Agent**   | Front-line — view + edit contacts, assign + close conversations.                       |

### Permission keys

Grouped exactly as the role editor renders them:

- **Users & roles** — `users.view`, `users.manage`, `roles.manage`
- **Contacts** — `contacts.view`, `contacts.edit`, `contacts.delete`
- **Conversations** — `conversations.assign`, `conversations.close`
- **Channels** — `channels.manage`
- **Settings** — `settings.view`
- **Billing** — `billing.view`, `billing.topup`, `billing.admin.refund`
- **Templates** — `templates.view`, `templates.create`, `templates.submit`, `templates.delete`
- **AI Agents** — `agents.view`, `agents.manage`
- **Platform Admin** — `platform.admin` *(cross-workspace, never seeded into a workspace role)*

### Platform Admin

`platform.admin` gates `/dashboard/platform-admin/*` and `/dashboard/audit/*` (cross-workspace stats + audit log). It's intentionally excluded from `Permissions.All`, so even a workspace Admin doesn't get it by default. To grant it, list the user's email in `appsettings.json`:

```json
"Platform": {
  "Admins": ["you@example.com"]
}
```

A seeder reads that list on startup, ensures a synthetic "Platform Admin" role exists, and assigns the listed users to it.

---

## 🌍 Localization

ChatCRM ships with **English, Russian, Romanian, Turkish** out of the box and uses a custom **JSON-file localizer** (not the standard RESX system) — drop a `strings.<culture>.json` file into `ChatCRM.MVC/Resources/` to add a new language.

**Culture is resolved in this order:**
1. `?culture=xx` query string — sets a one-year cookie when used
2. `.AspNetCore.Culture` cookie
3. `Accept-Language` request header
4. `en` fallback

**To switch languages from the UI:** use the language picker in the dashboard topbar (writes the cookie via `GET /language?culture=xx`).

**Where strings come from:**
- Server-rendered views: `IStringLocalizer<T>` injected via `JsonStringLocalizerFactory`
- JavaScript: a `window.resources` object is emitted into the layout; `i18n.js` exposes `t('Some.Key', args...)`

---

## 📋 Prerequisites

- **.NET 10 SDK** — https://dot.net/download
- **SQL Server LocalDB** (ships with Visual Studio, or install separately)
- **Redis** *(only if you wire up AI agents — see [AI agents](#-ai-agents-optional))*
- Optional for real WhatsApp: an **Evolution API** instance (see [Production setup](#-production-setup-real-whatsapp))
- Optional for billing testing: a **Stripe** test account (see [Stripe top-up](#-stripe-top-up-development-testing))
- Optional for templates: a **Meta WhatsApp Business** account + Graph API token

---

## 🚀 Quick start (mock mode — 2 minutes)

Mock mode uses an in-process fake WhatsApp backend — 3 seeded conversations and a simulator that fires an inbound message every 45 seconds. Perfect for UI work without linking a real phone, and the billing gate is bypassed so you don't need Stripe configured.

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

> 💡 You can also create + manage instances from the in-app **Settings → Channels** screen — it talks to `/api/instances/*` and renders the QR for you.

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

## 💳 Stripe top-up (development testing)

ChatCRM bills WhatsApp Cloud-API messages from a per-workspace wallet. Top-ups go through Stripe Checkout. To test the flow locally without a real card:

### 1. Get test-mode keys

Sign up at <https://dashboard.stripe.com/register> (free, no business verification needed for test mode). Then in the dashboard, switch to **Test mode** (toggle in the top right) and grab two values from **Developers → API keys**:

- **Publishable key** — starts with `pk_test_…`
- **Secret key** — starts with `sk_test_…`

Paste them into `ChatCRM.MVC/appsettings.Development.json`:

```json
"Stripe": {
  "PublishableKey": "pk_test_...",
  "SecretKey": "sk_test_...",
  "WebhookSecret": "",
  "MinAmountUsd": 10,
  "MaxAmountUsd": 1000
}
```

Leave `WebhookSecret` blank for now — we'll fill it from the Stripe CLI in step 3.

### 2. Install the Stripe CLI

It forwards Stripe webhooks to your localhost so you don't need a public URL just for billing testing.

- macOS: `brew install stripe/stripe-cli/stripe`
- Windows: `scoop install stripe` (or download from <https://github.com/stripe/stripe-cli/releases>)
- Linux: see <https://docs.stripe.com/stripe-cli>

Log in once: `stripe login`.

### 3. Forward webhooks to your local app

In a terminal, run:

```bash
stripe listen --forward-to https://localhost:7224/api/webhooks/stripe
```

The first line of output will be:

```
Ready! Your webhook signing secret is whsec_… (^C to quit)
```

Copy that `whsec_…` value into `Stripe:WebhookSecret` in `appsettings.Development.json`. Restart ChatCRM. Keep `stripe listen` running in the background — every event Stripe generates is forwarded to your local webhook URL with a real signature you can verify.

### 4. Make a test top-up

1. Sign in to ChatCRM as an Admin or Manager (the only roles with `billing.topup` by default).
2. Click the **balance pill** in the dashboard topbar → **Top up** → pick `$50` → **Continue to payment**.
3. You'll land on Stripe Checkout. Use one of Stripe's test cards:
   - **Success**: `4242 4242 4242 4242` · any future expiry · any CVC · any ZIP
   - **3-D Secure required**: `4000 0027 6000 3184`
   - **Decline**: `4000 0000 0000 0002`
4. After paying, Stripe redirects to `/dashboard/billing/topup/success`. Within seconds the webhook lands, the wallet credits, and the balance pill updates.

Verify:

```sql
SELECT BalanceUsd FROM Wallets WHERE WorkspaceId = 1;
SELECT TOP 1 Status, AmountUsd, BalanceAfterUsd, Reference FROM WalletTransactions ORDER BY Id DESC;
SELECT TOP 1 Action, AfterJson FROM BillingAuditLogs ORDER BY Id DESC;
```

Expected: balance went up by $50, latest transaction is `Status=Succeeded` with the Stripe `cs_test_…` session id, audit log entry is `topup.succeeded`.

### 5. Replay an event

If you want to test idempotency or a handler change:

```bash
stripe events resend evt_1Nxxxxxxxxxxxx
```

Re-delivered events are ignored (the `ProcessedStripeEvents` table short-circuits).

> 💡 **Don't put live keys (`sk_live_…`) in `appsettings.Development.json`.** Use environment variables in production: `Stripe__SecretKey`, `Stripe__PublishableKey`, `Stripe__WebhookSecret`.

---

## 🤖 AI agents (optional)

ChatCRM can auto-reply to incoming messages by routing them to an external **CRM-AI-Service** sidecar over HTTP + Redis pub/sub. If you don't want this, you can leave the AI config blank — the rest of the app works fine without it.

### How the loop works

```
Incoming msg → AiReplyDispatcher  ──► enqueue row in AiOutboxMessage
                                       │
              AiOutboxPublisher    ──► publishes job to Redis channel
                                       │
              CRM-AI-Service       ──► generates reply text
                                       │
              AiOutboundConsumer   ──► picks up reply ► ChatService.SendMessageAsync
                                                      ► hits Evolution API
```

### Required infra

- A reachable **Redis** instance (used for pub/sub between ChatCRM and the AI sidecar)
- A reachable **CRM-AI-Service** instance (separate repo) with its own API key

### Config

```json
{
  "Ai": {
    "BaseUrl": "https://your-crm-ai-service.example.com",
    "ApiKey": "your-ai-service-key",
    "RedisConnection": "localhost:6379"
  }
}
```

### Wiring an agent

1. Sign in as a user with `agents.manage` (Admin by default).
2. Go to **Agents** in the dashboard → **New agent** → fill in name, instructions, avatar, set as default if you want it to handle unassigned conversations.
3. On save, the agent is pushed to the AI service in the background. `RemoteSyncStatus` cycles `Pending → Synced` (or `Error` with a message you can retry).
4. Open a contact → **Assign agent** to route their conversation to a specific agent (overrides the workspace default).

---

## 🗄️ Database schema

Auto-applied at startup via `dbContext.Database.Migrate()`. There are **~30 tables** spanning seven subsystems; here's the map.

### Identity & access
| Table              | Notes                                                                          |
| ------------------ | ------------------------------------------------------------------------------ |
| `AspNetUsers`      | ASP.NET Identity + `FirstName`, `LastName`, `ProfileImagePath`, `IsActive`     |
| `AspNetRoles`      | RBAC roles (Admin, Manager, Agent, plus any custom)                            |
| `AspNetUserRoles`  | User ↔ role join                                                               |
| `AspNetRoleClaims` | Stores `Permission` claims (e.g. `billing.topup`) per role                     |
| `AspNetUserClaims` | Identity built-in (rarely used directly)                                       |
| `AspNetUserLogins` / `AspNetUserTokens` / `AspNetUserPasswords` | Identity built-ins        |

### WhatsApp & messaging
| Table                | Notes                                                                        |
| -------------------- | ---------------------------------------------------------------------------- |
| `WhatsAppInstances`  | Connected phone numbers — `Integration` (Personal/Business), status, JID, owner |
| `WhatsAppContacts`   | Phone, display name, country, language, lifecycle stage, blocked flag, email, company, job title, address, notes, assigned-agent, RemoteJid |
| `Conversations`      | `ContactId` + `InstanceId` (channel of origin), `AssignedUserId`, `AssignedAgentId`, `Status` (Open/Snoozed/Closed), `SnoozedUntil`, `LastMessageAt`, `LastIncomingAt` (24h window), `UnreadCount`, `IsArchived` |
| `Messages`           | `Body`, `Direction` (Incoming/Outgoing/Note), `Status` (Sent/Delivered/Read), `Kind` (Text/Image/Video/Audio/Document/Sticker), `MediaUrl`, `MediaMimeType`, `MediaFileName`, `EditedAt`, `IsDeleted`, `AuthorUserId`, `ExternalId` (unique — webhook dedup) |
| `Tags`               | Workspace-scoped tag (name + hex color)                                      |
| `ConversationTags`   | Many-to-many join between conversations and tags                             |

### Billing & wallet
| Table                    | Notes                                                                    |
| ------------------------ | ------------------------------------------------------------------------ |
| `Wallets`                | Per-workspace singleton — `BalanceUsd`, optimistic concurrency token, auto-recharge config, Stripe customer id |
| `WalletTransactions`     | Ledger — `Type` (Debit/Credit), `AmountUsd`, `BalanceAfterUsd`, `Reason`, `Status`, `Reference` (Stripe session id) |
| `BillingAuditLogs`       | Immutable audit trail — `Action`, `ActorUserId`, `EntityType`, `EntityId`, `BeforeJson`, `AfterJson` |
| `Invoices`               | Generated invoice with line items, total, PDF path, status               |
| `MessageBillingRecords`  | Per-message cost lookup (links Message → Meta pricing rule used)         |
| `MetaPricingRules`       | Cached Meta WhatsApp Cloud API rates per direction/type/region           |
| `BillingSettings`        | Global config: auto-recharge thresholds, retry policy, alert thresholds  |
| `ProcessedStripeEvents`  | Stripe webhook dedup — every event id ChatCRM has already handled        |

### Templates
| Table                | Notes                                                                       |
| -------------------- | --------------------------------------------------------------------------- |
| `WhatsAppTemplates`  | Local template — `Status` (Draft/Submitted/Approved/Rejected/Paused/Disabled/Stuck), Meta template id, placeholders, sample values, language, category |

### AI agents
| Table                | Notes                                                                       |
| -------------------- | --------------------------------------------------------------------------- |
| `Agents`             | Workspace-scoped — `Name`, `Instructions`, `AvatarPath`, `IsActive`, `IsDefault`, remote id + `RemoteSyncStatus`, `CreatedByUserId` |
| `AiOutboxMessages`   | Outbound reply queue — `ConversationId`, `Status`, `AttemptCount`, `LastError`, `ReplyText` |

**Key design decisions**

- `ExternalId` on `Messages` is unique with a filtered index — prevents duplicate webhook deliveries from creating dupe messages.
- `UnreadCount` is denormalized on `Conversation` so the sidebar can render without `COUNT(*)` per row.
- `Wallets.RowVersion` enables optimistic concurrency so two concurrent message-sends can't double-deduct.
- `ProcessedStripeEvents` is checked before processing any Stripe webhook — guarantees retried events don't double-credit.
- `BillingAuditLogs` is append-only — there's no update path in code.
- WhatsApp messages live in `AiOutboxMessages` until the AI consumer confirms send — survives process restarts.
- `WorkspaceId` is on every workspace-scoped entity even though v1 has one workspace — multi-tenancy is wired but not exposed.

---

## 🛣️ Routes & endpoints

### Public / auth
| Method   | Route                                | Auth | Purpose                                    |
| -------- | ------------------------------------ | ---- | ------------------------------------------ |
| GET      | `/`                                  | —    | Landing page                               |
| GET/POST | `/Account/Register`                  | —    | Create account                             |
| GET/POST | `/Account/Login`                     | —    | Authenticate                               |
| GET      | `/Account/ConfirmEmail`              | —    | Email verification callback                |
| GET/POST | `/Account/ForgotPassword`            | —    | Reset request                              |
| GET/POST | `/Account/ResetPassword`             | —    | Reset form                                 |
| GET/POST | `/Account/Profile`                   | ✅   | View / edit profile                        |
| POST     | `/Account/Logout`                    | ✅   | Sign out                                   |
| GET      | `/language?culture=xx`               | —    | Switch UI language (cookie)                |

### Chat
| Method | Route                                | Auth | Purpose                                    |
| ------ | ------------------------------------ | ---- | ------------------------------------------ |
| GET    | `/dashboard/chats`                   | ✅   | **Main dashboard**                         |
| GET    | `/dashboard/chats/{id}/messages`     | ✅   | Fetch messages for one conversation (JSON) |
| GET    | `/dashboard/chats/{id}/contact`      | ✅   | Conversation-shaped contact details        |
| POST   | `/dashboard/chats/send`              | ✅   | Send a reply                               |
| WS     | `/hubs/chat`                         | ✅   | SignalR hub                                |

### Contacts
| Method | Route                                  | Auth                 | Purpose                                       |
| ------ | -------------------------------------- | -------------------- | --------------------------------------------- |
| GET    | `/dashboard/contacts`                  | ✅                   | Contacts table page                           |
| GET    | `/api/contacts`                        | ✅                   | List (search, filter, paging)                 |
| GET    | `/api/contacts/{id}/details`           | ✅                   | Lead-card details (works without conversation) |
| POST   | `/api/contacts/{id}/lifecycle`         | ✅                   | Set lifecycle stage                           |
| POST   | `/api/contacts/{id}/assign`            | ✅                   | Assign user / unassign                        |
| POST   | `/api/contacts/{id}/status`            | ✅                   | Open / close conversation                     |
| POST   | `/api/contacts/{id}/language`          | ✅                   | Set preferred language                        |
| POST   | `/api/contacts/{id}/block`             | ✅                   | Block / unblock                               |
| DELETE | `/api/contacts/{id}`                   | ✅                   | Delete                                        |
| GET    | `/api/contacts/export`                 | ✅                   | CSV of the current filter                     |
| GET    | `/api/contacts/import/template`        | ✅                   | Download empty Excel template                 |
| POST   | `/api/contacts/import/preview`         | ✅                   | Parse + validate uploaded file, return preview |
| POST   | `/api/contacts/import/confirm`         | ✅                   | Bulk insert validated rows (transactional)    |
| POST   | `/api/contacts/import/error-report`    | ✅                   | Download Excel report of failed rows          |

### Billing
| Method | Route                                            | Permission              | Purpose                                |
| ------ | ------------------------------------------------ | ----------------------- | -------------------------------------- |
| GET    | `/dashboard/billing`                             | `billing.view`          | Wallet + transactions overview         |
| GET    | `/dashboard/billing/topup`                       | `billing.topup`         | Top-up form                            |
| POST   | `/dashboard/billing/topup`                       | `billing.topup`         | Create Stripe Checkout session         |
| GET    | `/dashboard/billing/topup/success`               | `billing.topup`         | Stripe return URL                      |
| GET    | `/dashboard/billing/topup/cancel`                | `billing.topup`         | Stripe cancel URL                      |
| POST   | `/dashboard/billing/setup-intent`                | `billing.topup`         | Stripe SetupIntent for saving a card   |
| POST   | `/dashboard/billing/setup-intent/confirm`        | `billing.topup`         | Confirm SetupIntent                    |
| GET    | `/dashboard/billing/autorecharge`                | `billing.view`          | Auto-recharge config view              |
| POST   | `/dashboard/billing/autorecharge`                | `billing.admin.refund`  | Update auto-recharge rules             |
| POST   | `/dashboard/billing/adjust`                      | `billing.admin.refund`  | Manual credit/debit (Admin only)       |
| GET    | `/dashboard/billing/analytics`                   | `billing.view`          | Analytics page                         |
| GET    | `/dashboard/billing/analytics/data`              | `billing.view`          | Analytics JSON                         |
| GET    | `/dashboard/billing/transactions`                | `billing.view`          | Paged transactions                     |
| GET    | `/dashboard/billing/transactions.csv`            | `billing.view`          | Transactions CSV export                |

### Invoices
| Method | Route                                            | Auth | Purpose                                |
| ------ | ------------------------------------------------ | ---- | -------------------------------------- |
| GET    | `/dashboard/billing/invoices`                    | ✅   | Invoice list page                      |
| GET    | `/dashboard/billing/invoices/list`               | ✅   | Invoices JSON                          |
| GET    | `/dashboard/billing/invoices/{id}`               | ✅   | Invoice detail JSON                    |
| POST   | `/dashboard/billing/invoices/generate`           | ✅   | Generate draft invoice for a period    |
| POST   | `/dashboard/billing/invoices/{id}/issue`         | ✅   | Issue the draft (lock it)              |
| GET    | `/dashboard/billing/invoices/{id}.pdf`           | ✅   | Download invoice PDF                   |

### Templates
| Method | Route                                       | Permission             | Purpose                          |
| ------ | ------------------------------------------- | ---------------------- | -------------------------------- |
| GET    | `/dashboard/templates`                      | `templates.view`       | Template library page            |
| GET    | `/dashboard/templates/list`                 | `templates.view`       | Templates JSON                   |
| GET    | `/dashboard/templates/{id}`                 | `templates.view`       | One template                     |
| GET    | `/dashboard/templates/state`                | `templates.view`       | Bulk status snapshot             |
| GET    | `/dashboard/templates/approved`             | `templates.view`       | List approved (for sending)      |
| POST   | `/dashboard/templates/draft`                | `templates.create`     | Create draft template            |
| POST   | `/dashboard/templates/{id}/draft`           | `templates.create`     | Update draft                     |
| POST   | `/dashboard/templates/{id}/submit`          | `templates.submit`     | Submit to Meta for approval      |
| POST   | `/dashboard/templates/{id}/delete`          | `templates.delete`     | Delete                           |
| POST   | `/dashboard/templates/sync`                 | `templates.submit`     | Force-sync all statuses from Meta |

### Agents
| Method | Route                                              | Permission       | Purpose                          |
| ------ | -------------------------------------------------- | ---------------- | -------------------------------- |
| GET    | `/dashboard/agents`                                | `agents.view`    | Agents page                      |
| GET    | `/dashboard/agents/list`                           | `agents.view`    | Agents JSON                      |
| GET    | `/dashboard/agents/{id}`                           | `agents.view`    | One agent                        |
| GET    | `/dashboard/agents/picker`                         | `agents.view`    | Picker modal (HTML fragment)     |
| POST   | `/dashboard/agents`                                | `agents.manage`  | Create                           |
| POST   | `/dashboard/agents/{id}`                           | `agents.manage`  | Update                           |
| POST   | `/dashboard/agents/{id}/avatar`                    | `agents.manage`  | Upload avatar                    |
| POST   | `/dashboard/agents/{id}/default`                   | `agents.manage`  | Set as workspace default         |
| POST   | `/dashboard/agents/{id}/active`                    | `agents.manage`  | Toggle active                    |
| POST   | `/dashboard/agents/{id}/delete`                    | `agents.manage`  | Delete                           |
| POST   | `/dashboard/contacts/{contactId}/agent`            | `agents.view`    | Assign agent to a contact        |
| POST   | `/dashboard/conversations/{conversationId}/agent`  | `agents.view`    | Assign agent to a conversation   |

### Users & roles
| Method | Route                              | Auth                  | Purpose                            |
| ------ | ---------------------------------- | --------------------- | ---------------------------------- |
| GET    | `/dashboard/settings/users`        | `users.view`          | Users table page                   |
| GET    | `/api/users`                       | `users.view`          | List                               |
| GET    | `/api/users/{id}`                  | `users.view`          | One user                           |
| POST   | `/api/users`                       | `users.manage`        | Create                             |
| PUT    | `/api/users/{id}`                  | `users.manage`        | Update (incl. role + permissions)  |
| POST   | `/api/users/{id}/active`           | `users.manage`        | Toggle active                      |
| DELETE | `/api/users/{id}`                  | `users.manage`        | Delete                             |
| GET    | `/dashboard/settings/roles`        | `roles.manage`        | Roles editor                       |
| GET    | `/api/roles`                       | `roles.manage`        | List                               |
| GET    | `/api/roles/{id}`                  | `roles.manage`        | One role                           |
| POST   | `/api/roles`                       | `roles.manage`        | Create / update (name + claims)    |
| DELETE | `/api/roles/{id}`                  | `roles.manage`        | Delete                             |

### Channels (WhatsApp instances)
| Method | Route                              | Auth | Purpose                          |
| ------ | ---------------------------------- | ---- | -------------------------------- |
| GET    | `/dashboard/settings/channels`     | ✅   | Channels page                    |
| GET    | `/api/instances`                   | ✅   | List                             |
| GET    | `/api/instances/{id}`              | ✅   | One instance                     |
| POST   | `/api/instances`                   | ✅   | Create                           |
| GET    | `/api/instances/{id}/qr`           | ✅   | Fetch QR (base64 PNG)            |
| GET    | `/api/instances/{id}/status`       | ✅   | Poll connection status           |
| POST   | `/api/instances/{id}/disconnect`   | ✅   | Disconnect WhatsApp link         |
| DELETE | `/api/instances/{id}`              | ✅   | Delete                           |

### Platform admin (cross-workspace)
| Method | Route                                 | Permission        | Purpose                       |
| ------ | ------------------------------------- | ----------------- | ----------------------------- |
| GET    | `/dashboard/platform-admin`           | `platform.admin`  | Platform stats dashboard      |
| GET    | `/dashboard/platform-admin/overview`  | `platform.admin`  | Overview JSON (date range)    |
| GET    | `/dashboard/audit`                    | `platform.admin`  | Audit log viewer page         |
| GET    | `/dashboard/audit/query`              | `platform.admin`  | Paged audit log JSON          |
| GET    | `/dashboard/audit/filters`            | `platform.admin`  | Distinct actions + entity types |

### Webhooks
| Method | Route                              | Auth   | Purpose                                       |
| ------ | ---------------------------------- | ------ | --------------------------------------------- |
| POST   | `/api/evolution/webhook`           | 🔒¹    | Evolution API → ChatCRM (inbound + status)    |
| POST   | `/api/webhooks/stripe`             | 🔒²    | Stripe → ChatCRM (top-up payment events)      |

¹ Secured by `x-webhook-secret` header matching `Evolution:WebhookSecret`.
² Secured by Stripe signature verification against `Stripe:WebhookSecret`.

---

## 📁 Project structure

```
ChatCRM/
├── ChatCRM.Domain/                         Pure entities — no framework deps
│   └── Entities/
│       ├── User.cs                         ASP.NET Identity + profile fields
│       ├── WhatsAppContact.cs              Contact + lifecycle + import fields
│       ├── WhatsAppInstance.cs             Connected number (Personal/Business)
│       ├── Conversation.cs                 + Status, SnoozedUntil, LastIncomingAt
│       ├── Message.cs                      + Kind, Media*, EditedAt, IsDeleted, AuthorUserId
│       ├── Tag.cs + ConversationTag.cs     Conversation tagging
│       ├── Agent.cs                        Workspace AI agent
│       ├── AiOutboxMessage.cs              AI reply queue
│       ├── Wallet.cs + WalletTransaction.cs   Billing ledger
│       ├── BillingAuditLog.cs              Immutable audit trail
│       ├── Invoice.cs                      PDF-generated invoice
│       ├── MessageBillingRecord.cs         Per-message cost
│       ├── MetaPricingRule.cs              Cached Meta rates
│       ├── BillingSettings.cs              Auto-recharge config
│       ├── ProcessedStripeEvent.cs         Webhook dedup
│       ├── WhatsAppTemplate.cs             Template library
│       ├── Permissions.cs                  RBAC permission keys + role labels
│       ├── ChannelType.cs                  Enum: WhatsApp, Instagram, … (multi-channel ready)
│       └── LifecycleStage.cs               Enum: NewClient → OurClient
│
├── ChatCRM.Application/                    DTOs, interfaces, validators
│   ├── Interfaces/                         IChatService, IEvolutionService, IContactsService,
│   │                                       IContactImportService, IWalletService, IBillingGate,
│   │                                       IPaymentProvider, IInvoiceService, IPricingService,
│   │                                       IAgentService, IAiAgentClient, ITemplateService,
│   │                                       IWhatsAppTemplateProvider, IAuditLogService,
│   │                                       IPlatformAdminService, IUserManagementService,
│   │                                       IRoleManagementService, IWhatsAppInstanceService, …
│   ├── Users/                              Login, Register, ResetPassword DTOs
│   ├── Chats/                              Conversation, Message, ContactDetails DTOs
│   ├── Contacts/                           Contact list + Import DTOs, parser, validator
│   ├── Agents/, Ai/, Billing/, Dashboard/, Per-subsystem DTOs
│   │   Templates/
│
├── ChatCRM.Persistence/                    EF Core
│   ├── AppDbContext.cs
│   └── Migrations/                         ~30 migrations (see schema timeline below)
│
├── ChatCRM.Infrastructure/                 External-facing services
│   ├── Authorization/                      PermissionRequirement.cs (handler + attribute)
│   ├── Hubs/ChatHub.cs                     SignalR hub
│   └── Services/
│       ├── ChatService.cs                  Send / fetch / mark-read
│       ├── EvolutionService.cs             Real Evolution API client
│       ├── MockEvolutionService.cs         Dev-only no-op
│       ├── DemoDataSeeder.cs               3 contacts + messages on first run (mock mode)
│       ├── FakeMessageSimulator.cs         Inbound message every 45s (mock mode)
│       ├── ContactsService.cs              Contact CRUD + lifecycle
│       ├── ContactImportService.cs         Excel parser → validate → bulk insert
│       ├── WhatsAppInstanceService.cs      Instance CRUD + Evolution sync
│       ├── UserManagementService.cs        User CRUD + role assignment
│       ├── PhoneCountryDetector.cs         Phone code → country mapping
│       ├── PricingService.cs               Meta pricing-rule lookup
│       ├── WalletService.cs                Balance + ledger
│       ├── RoleSeeder.cs / BillingSeeder.cs / InstanceSeeder.cs   Startup seeders
│       ├── Billing/                        BillingGate, autorecharge worker
│       ├── Payments/                       StripePaymentProvider, StripeOptions
│       ├── Invoices/                       InvoiceService, QuestPdfInvoiceRenderer
│       ├── Templates/                      TemplateService, MetaGraphTemplateProvider,
│       │                                   TemplateStatusSyncService, TemplateProviderHealth,
│       │                                   MetaGraphOptions
│       ├── Agents/                         AgentService, AiAgentClient, AiOutboxPublisher,
│       │                                   AiOutboundConsumer, AiReplyDispatcher, AiOptions
│       ├── Ai/                             Shared AI helpers
│       ├── Audit/                          AuditLogService
│       └── Admin/                          PlatformAdminService
│
├── ChatCRM.MVC/                            ASP.NET Core web app (entry point)
│   ├── Controllers/                        17 controllers — Account, Dashboard, Contacts,
│   │                                       Billing, Invoices, Templates, Agents, Users, Roles,
│   │                                       Settings, Instances, Audit, PlatformAdmin,
│   │                                       Webhook, StripeWebhook, Home, Language
│   ├── Views/
│   │   ├── Account/                        Login, Register, Profile, ResetPassword, …
│   │   ├── Dashboard/                      Chats (main), Index, WhatsApp
│   │   ├── Contacts/                       Index (table + import dialog)
│   │   ├── Billing/                        Index, TopUp, TopUpSuccess, TopUpCancel, Analytics
│   │   ├── Templates/, Agents/, Users/,    Per-subsystem views
│   │   │   Roles/, Invoices/, Audit/,
│   │   │   PlatformAdmin/, Settings/
│   │   ├── Home/                           Landing + Privacy
│   │   └── Shared/                         _Layout, _AuthLayout, _LandingLayout,
│   │                                       _DashboardRail, _SettingsSidenav, _BalancePill,
│   │                                       _LanguageSwitcher, _Icon, Error
│   ├── Resources/                          strings.{en,ru,ro,tr}.json
│   ├── Localization/                       JsonStringLocalizer + factory
│   ├── Services/                           SmtpEmailSender, ProfileImageStorageService,
│   │                                       BillingEmailSender
│   ├── wwwroot/
│   │   ├── css/                            chat.css, contacts.css, dashboard.css, …
│   │   └── js/                             chat.js, contacts.js, contacts-import.js,
│   │                                       contacts-agent-picker.js, users.js, roles.js,
│   │                                       templates.js, agents.js, invoices.js, audit.js,
│   │                                       platform-admin.js, instances.js, i18n.js, site.js
│   ├── Program.cs                          DI + middleware + DB migrate + seeders
│   ├── appsettings.json                    Committed — no secrets!
│   └── appsettings.Development.json        Gitignored — real secrets here
│
├── ChatCRM.Common/                         Reserved for cross-cutting utilities
│
├── ChatCRM.Tests/                          xUnit tests
│   └── Contacts/Import/
│       ├── ContactImportParserTests.cs     Parser: header reorder, whitespace, missing headers
│       └── ImportRowValidatorTests.cs      Validator: phone normalization, email, dedup
│
├── docker/
│   └── docker-compose.yml                  Self-hosted Evolution API + Postgres + Redis
│
└── README.md
```

---

## ⚙️ Configuration reference

### Settings layering

Configuration is loaded in this order (later overrides earlier):
1. `appsettings.json` — committed defaults
2. `appsettings.Development.json` — **gitignored**, put real secrets here
3. Environment variables — for production / CI (use `__` as section separator: `Stripe__SecretKey`)

### Keys by section

#### Core
| Key                                          | Purpose                                                    |
| -------------------------------------------- | ---------------------------------------------------------- |
| `ConnectionStrings:DefaultConnection`        | EF Core connection string                                  |
| `Smtp:Host` / `Port` / `EnableSsl` / `FromEmail` / `FromName` | Outgoing email                            |
| `Smtp:Username` / `Password`                 | Dev-only — put in `appsettings.Development.json`           |

#### Evolution (WhatsApp bridge)
| Key                          | Purpose                                                        |
| ---------------------------- | -------------------------------------------------------------- |
| `Evolution:UseMock`          | `true` = no real WhatsApp; `false` = use Evolution API         |
| `Evolution:BaseUrl`          | Evolution API base URL (no trailing slash)                     |
| `Evolution:ApiKey`           | `AUTHENTICATION_API_KEY` from the Evolution instance           |
| `Evolution:InstanceName`     | Your instance name, e.g. `chatcrm`                             |
| `Evolution:WebhookSecret`    | Any strong string — must match what you register with Evolution |

#### Stripe (billing)
| Key                          | Purpose                                                          |
| ---------------------------- | ---------------------------------------------------------------- |
| `Stripe:PublishableKey`      | `pk_test_…` / `pk_live_…`                                        |
| `Stripe:SecretKey`           | `sk_test_…` / `sk_live_…`                                        |
| `Stripe:WebhookSecret`       | `whsec_…` from `stripe listen` or your Stripe dashboard          |
| `Stripe:MinAmountUsd`        | Minimum per-top-up amount                                        |
| `Stripe:MaxAmountUsd`        | Maximum per-top-up amount                                        |

#### Meta Graph (templates) — optional
| Key                                | Purpose                                                  |
| ---------------------------------- | -------------------------------------------------------- |
| `MetaGraph:AccessToken`            | Meta Business Manager system-user access token           |
| `MetaGraph:BusinessAccountId`      | WhatsApp Business Account (WABA) id                      |
| `MetaGraph:ApiVersion`             | Graph API version (e.g. `v20.0`)                         |

#### AI agents — optional
| Key                          | Purpose                                                          |
| ---------------------------- | ---------------------------------------------------------------- |
| `Ai:BaseUrl`                 | URL of the external CRM-AI-Service                               |
| `Ai:ApiKey`                  | API key for the AI sidecar                                       |
| `Ai:RedisConnection`         | Redis connection string (used for AI pub/sub)                    |

#### Platform admin
| Key                          | Purpose                                                          |
| ---------------------------- | ---------------------------------------------------------------- |
| `Platform:Admins`            | Array of email addresses granted `platform.admin` on startup     |

---

## 🧪 Tests

The `ChatCRM.Tests` project uses **xUnit** and covers the contact-import parser and validator (the highest-risk path because it accepts untrusted Excel input).

```bash
dotnet test ChatCRM.Tests/ChatCRM.Tests.csproj
```

Current coverage:
- **`ContactImportParserTests`** — header-name matching (case + whitespace tolerant), reordered columns, missing required headers, empty rows, blank cells, oversized cells
- **`ImportRowValidatorTests`** — phone normalization (E.164), email format, required-field checks, length caps, per-row error accumulation

27 tests pass on the current branch.

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

**"Insufficient balance" when sending a message**
The wallet (Cloud-API only) doesn't have enough to cover the Meta cost for that message. Top up via the balance pill in the topbar (requires `billing.topup`). In mock mode the gate is bypassed.

**Stripe webhook arrives but balance doesn't update**
- Confirm `stripe listen` is still running and is forwarding to the right URL.
- Check the `ProcessedStripeEvents` table — if the event id is there, it's been processed already (and probably succeeded — check `WalletTransactions`).
- Inspect `BillingAuditLogs` for entries with action `topup.failed` and look at `AfterJson` for the error reason.

**`AiOutboxMessage` rows pile up with `Status = Pending`**
Either the AI sidecar (`CRM-AI-Service`) is unreachable, or `Ai:RedisConnection` is wrong. Check the app logs for repeated `AiOutboundConsumer` errors.

**`ngrok-agent version too old`**
```
ngrok update
```
Free accounts require agent v3.20+.

**Database errors on first run**
The app auto-runs `Database.Migrate()` on startup. If that fails, check:
- SQL Server LocalDB is installed and running
- The connection string in `appsettings.json` points to a reachable server
- The user running the app has DB-create permissions

---

## 🔒 Security & legal

### Secrets management
- **Never commit real Evolution API keys, Stripe keys, Meta tokens, or SMTP passwords.**
- `appsettings.Development.json` is in `.gitignore` for exactly this reason.
- For production use [.NET User Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) or environment variables (`Stripe__SecretKey`, `Evolution__ApiKey`, etc.).

### WhatsApp terms of service
Evolution API uses the **unofficial WhatsApp Web protocol** (Baileys). This technically **violates WhatsApp's Terms of Service**. Risks:
- Your linked phone number may be banned without warning.
- Meta can (and does) break the protocol, causing silent downtime.

**For any real business use:** apply for the official **[Meta WhatsApp Cloud API](https://developers.facebook.com/docs/whatsapp/cloud-api)** via Facebook Business Manager. ChatCRM's `WhatsAppInstance.Integration = Business` mode already supports it — flip the integration type when creating the instance.

### Built-in security features
- ✅ ASP.NET Identity password hashing (PBKDF2)
- ✅ CSRF protection (`[ValidateAntiForgeryToken]`)
- ✅ HttpOnly + SameSite cookies
- ✅ Account lockout after 5 failed attempts
- ✅ Email verification required for login
- ✅ Claim-based RBAC with per-action `[RequirePermission(...)]` gates
- ✅ Evolution webhook authenticated via shared `x-webhook-secret`
- ✅ Stripe webhook authenticated via signature verification
- ✅ Stripe event deduplication (`ProcessedStripeEvents` table) prevents double-credit
- ✅ Wallet optimistic concurrency prevents double-debit on concurrent sends
- ✅ Text sanitization on user input
- ✅ Path-traversal protection on profile / agent avatar uploads
- ✅ Immutable billing audit log (append-only by design)

---

## 📄 License

Private / unpublished.

## 🙌 Credits

- [Evolution API](https://github.com/EvolutionAPI/evolution-api) — WhatsApp integration layer
- [Baileys](https://github.com/WhiskeySockets/Baileys) — underlying WhatsApp Web client
- [ASP.NET Core](https://dotnet.microsoft.com) — framework
- [SignalR](https://dotnet.microsoft.com/apps/aspnet/signalr) — real-time messaging
- [Stripe.NET](https://github.com/stripe/stripe-dotnet) — payment processing
- [QuestPDF](https://www.questpdf.com) — invoice PDF generation
- [ClosedXML](https://github.com/ClosedXML/ClosedXML) — Excel parsing for contact import
- [FluentValidation](https://fluentvalidation.net) — input validation
