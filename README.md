# MTG Commander Game Tracker

A starter ASP.NET Core Razor Pages app for tracking Commander games locally with SQLite.

## What it includes

- Razor Pages web UI for phone-friendly entry
- SQLite database via Entity Framework Core
- data model for people, decks, games, participants, and winners
- basic pages for recent games, add game, people, and decks

## Run locally

1. Open a terminal in `mtg-game-tracker\src`
2. Run `dotnet restore`
3. Run `dotnet run`
4. Open `http://localhost:5000` in your browser
> Requires .NET 10 SDK installed.
## Mobile access

From a phone on the same home Wi-Fi, open `http://<PC_IP>:5000`.

## Database file

The SQLite file is created at `App_Data/mtg-games.db` by default. Update `ConnectionStrings:DefaultConnection` in `appsettings.json` to point to a Google Drive sync folder for backup.
