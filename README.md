<img src="web/public/logo.svg" width="96" alt="MailGester logo">

# MailGester

**Never miss the mail that matters.**

Private by design: your mail is read by a local AI on your own machine, only the digest goes to Discord, and stored credentials are encrypted at rest.

Daily digest of several Gmail and Outlook accounts, junk folders included. A local Ollama model writes the summary and decides whether to ping a Discord role. Everything is configured in the web UI.

## Run

```
docker compose up -d --build
```

Open `http://<this PC's address>:8080`. If port 8080 is taken, set another one first, for example `MAILGESTER_PORT=8090` in a `.env` file next to the compose file. The first visit asks you to choose an admin password.

A step by step guide for Discord, Ollama, Gmail and Outlook is in [SETUP.md](SETUP.md). The Outlook part needs a free Microsoft Entra app registration, which the guide walks through.

## How the model and the code split the work

- The summary is always posted to Discord.
- The model returns `notify`, `urgency`, `notifyReason` and a `noteworthy` list (by mail id). The role is pinged only when `notify` is true.
- Safety nets in code, independent of the model:
  - Priority keywords are matched in code. A hit in a junk folder pings even if the model stayed quiet (setting).
  - A mailbox that cannot be read is reported in the post and pings.
  - If the model fails three times in a scheduled run, a degraded post with keyword matches is sent instead of nothing.
- Sender and subject in the post come from the original mail, not from the model. Mail derived text is neutralized so it cannot render as a link.
- Junk mail is marked unverified in the prompt, and mail content is declared untrusted data. See `server/Digest/DigestPrompt.cs`.
- Each mailbox has its own cursor. If the PC was off at the scheduled time, the digest runs when the app starts again.

## Branding

Colours, wordmark and tone are in [BRAND.md](BRAND.md). Use `web/public/logo-512.png` as the avatar of the Discord webhook, since webhooks cannot take an avatar from the app.

## Without the bundled Ollama

Delete the `ollama` service from `docker-compose.yml` and set the address of your own Ollama under Settings, Ollama. Nothing else depends on it.

## Security notes

- The UI is plain http on port 8080, reachable from your LAN. The password and session cookie are not encrypted in transit, so only use it on a network you trust, or put a reverse proxy with TLS in front, or reach it over a VPN such as Tailscale.
- Gmail app passwords, the Discord webhook and the Outlook token cache are encrypted at rest with ASP.NET Data Protection. The keys are in the data volume next to the data, so protect the volume itself.
- Back up the `app-data` volume. Losing the keys means the stored secrets cannot be read again.
- Logins are throttled after five failed attempts.

## Development

```
cd server && DataDirectory=./data dotnet run --urls http://localhost:8080
cd web && npm install && npm start
```

`npm start` serves the UI on port 4200 and proxies `/api` to the server.

## License

Licensed under the [PolyForm Noncommercial License 1.0.0](LICENSE). Free to use, download and modify for noncommercial purposes. Copyright 2026 Kevin Borup.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Contributions include a grant that lets the maintainer relicense them, so read it before opening a pull request.
