# CW Notification Companion

A Windows system tray application that monitors ConnectWise Manage for tickets awaiting client response and surfaces them in an always-on-top notification window.

## Features

- Runs silently in the system tray
- Polls ConnectWise on a configurable interval
- Shows an always-on-top window listing tickets in **"Client Responded"** or **"Client Responded - 2"** status
- One-click **Open in CW** button opens the ticket directly in your default browser
- Settings window for API credentials and preferences

## Requirements

- Windows 10 / 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- A ConnectWise Manage API key (Company ID + Public Key + Private Key)
- A registered Client ID from [developer.connectwise.com](https://developer.connectwise.com)

## Setup

1. Download and run the latest release.
2. The Settings window opens automatically on first launch.
3. Fill in your ConnectWise API credentials:

| Field | Description |
|---|---|
| **Server URL** | e.g. `https://na.myconnectwise.net/v4_6_release/apis/3.0` |
| **Company ID** | The company name used to log in to ConnectWise Manage |
| **Public Key** | API member public key |
| **Private Key** | API member private key |
| **Client ID** | GUID from developer.connectwise.com |
| **Company URL Slug** | The `companyName` parameter in CW ticket URLs |
| **Poll Interval** | How often (minutes) to check for new responses |

4. Click **Test Connection** to verify, then **Save Settings**.

## Authentication

The app uses ConnectWise's Basic Authentication scheme:

```
Authorization: Basic base64(CompanyId+PublicKey:PrivateKey)
clientId: <your-client-id>
```

## Building from Source

```bash
dotnet build CWNotificationCompanion.sln
dotnet publish CWNotificationCompanion/CWNotificationCompanion.csproj -c Release -r win-x64 --self-contained false
```

## Settings Storage

Settings are stored in `%AppData%\CWNotificationCompanion\settings.json`. The private key is stored in plain text — ensure the file is protected by OS-level permissions.

## Ticket URL Format

Tickets open at:

```
https://api-na.myconnectwise.net/v4_6_release/services/system_io/router/openrecord.rails
  ?locale=en_US
  &companyName={CompanySlug}
  &recordType=ServiceFV
  &recid={TicketId}
```
