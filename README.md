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

## Local data and upgrades

Both the personal and public builds use `%LOCALAPPDATA%\MyClinicApp\ClinicData.db` for production data and `%LOCALAPPDATA%\MyClinicApp\PatientVisitImages` for visit images. The installer keeps this folder on uninstall and excludes SQLite database files from the application install directory, so installing the public build over the personal build reuses the existing clinic data instead of replacing it.

Close the app before switching versions. As an extra safeguard, make a private backup of the entire `MyClinicApp` folder before installing; it can contain patient data and Google Drive credentials and must not be uploaded or committed. Keep the backup in a secure location.

## Update notices

The app checks the GitHub repository's latest published release when the main window opens and also provides a manual update check. To notify users of a new build, publish a GitHub release with a version tag newer than the app version (for example, `v1.7.0`) and attach the installer executable. Users are prompted to open the installer download; the app does not install updates silently.

## Security

Do not commit real patient data, database files, OAuth tokens, or other credentials. Use a separate Google Cloud project for development and production.
