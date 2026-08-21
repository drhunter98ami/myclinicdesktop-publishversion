# My Clinic

My Clinic is a Windows desktop clinic-management application built with WPF, .NET, and SQLite.

## Google Drive setup

The application uses Google OAuth so every user can connect their own Google Drive account. End-user Google passwords are never stored by the application.

For local development:

1. Create or select a Google Cloud project.
2. Enable the Google Drive API.
3. Create an OAuth client for a **Desktop app**.
4. Download the client JSON and save it beside the project file as `credentials.json`.
5. Keep the OAuth consent screen in Testing while developing, or publish it to Production before distributing the application publicly.

`credentials.json`, OAuth tokens, local databases, patient images, build output, and workspace attachments are intentionally excluded from Git.

## Build

```powershell
dotnet restore
dotnet build
```

To create a Windows publish folder:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

Ensure the production `credentials.json` is present in the publish output before creating the installer. Do not commit it to GitHub.

## Security

Do not commit real patient data, database files, OAuth tokens, or other credentials. Use a separate Google Cloud project for development and production.
