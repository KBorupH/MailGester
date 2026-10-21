# MailGester setup guide

This guide takes you from a fresh clone to a working daily digest. Do the parts in order. Each part says what you should see when it worked.

Parts:

1. [Start the containers](#1-start-the-containers)
2. [Create the admin password](#2-create-the-admin-password)
3. [Discord](#3-discord)
4. [Ollama and the model](#4-ollama-and-the-model)
5. [Gmail accounts](#5-gmail-accounts)
6. [Outlook accounts](#6-outlook-accounts)
7. [Tell the AI what matters](#7-tell-the-ai-what-matters)
8. [First run](#8-first-run)
9. [Daily use](#9-daily-use)
10. [Troubleshooting](#10-troubleshooting)

## 1. Start the containers

You need Docker with Compose. For the bundled Ollama to use the GPU you also need the NVIDIA Container Toolkit. On Windows that means Docker Desktop with WSL2 and a current NVIDIA driver.

```
docker compose up -d --build
```

The first run downloads the base images and builds the app, so it takes a few minutes.

If port 8080 is already used on your PC, either edit the left number in the `ports` line of `docker-compose.yml`, or create a file named `.env` next to it containing:

```
MAILGESTER_PORT=8090
```

Then run `docker compose up -d` again.

You should see two containers running: `app` and `ollama`. Check with `docker compose ps`.

## 2. Create the admin password

Open `http://localhost:8080` (or the port you chose). From another device on your network, use the PC's address instead of `localhost`.

The first visit asks you to choose an admin password. Use at least 10 characters. After that, the same page asks you to sign in.

The page is plain http. Only use it on a network you trust.

## 3. Discord

MailGester posts the digest through a webhook and pings a role.

### Webhook

1. In your Discord server, open the channel for the digest.
2. Open Edit Channel, then Integrations, then Webhooks, then New Webhook.
3. Name it and, if you like, upload `web/public/logo-512.png` as its avatar. A webhook cannot take its avatar from the app.
4. Copy the Webhook URL.

You need the Manage Webhooks permission in that channel.

### Role

1. Create a role for the pings, for example `MailGester`, in Server Settings, Roles. Give it to yourself.
2. Turn on Developer Mode in Discord (User Settings, Advanced).
3. Right click the role in Server Settings, Roles and choose Copy Role ID.

### In MailGester

1. Open Settings, Discord.
2. Paste the webhook URL. It must be a `discord.com` or `discordapp.com` address.
3. Paste the role ID. Digits only, not the role name.
4. Click Save and send test ping.

You should see a test message in the channel that mentions your role.

If the mention appears but you get no notification, check that you hold the role, the channel is not muted and your Discord notification settings allow role mentions. If the role shows as plain text, enable "Allow anyone to @mention this role" on the role.

Leave the role ID empty if you never want a ping. The summary still posts.

## 4. Ollama and the model

1. Open Settings, Ollama.
2. Leave the address as `http://ollama:11434` to use the bundled service.
3. Leave the model as `qwen2.5:14b` for a first run. It needs about 9 GB of video memory.
4. Click Save and download model. The download is large, so wait until it finishes.
5. Click Save and check to confirm that MailGester can reach Ollama and find the model.

### Choosing another model

The model is a setting, so you can change it later and compare results with Run now on the Dashboard. Pick a model that returns structured JSON reliably and handles the languages your mail is written in. Check what fits your video memory. If the model plus context does not fit on the GPU, Ollama moves part of it to system memory and runs much slower. One digest a day can tolerate that, but you can also lower the Context size.

Models with a thinking mode may behave differently with structured output, so test them before relying on them.

### Using an Ollama that runs elsewhere

Delete the `ollama` service from `docker-compose.yml`, run `docker compose up -d` and set the address under Settings, Ollama to the other machine, for example `http://192.168.1.20:11434`.

## 5. Gmail accounts

Gmail needs an app password. Your normal password does not work.

1. Turn on 2 Step Verification for the Google account.
2. Open `myaccount.google.com/apppasswords`. If you cannot find it, search for "App passwords" in your Google Account settings.
3. Create a password, name it MailGester and copy the 16 character code.
4. In MailGester open Accounts, choose the Gmail type, enter the email address and the app password, and click Add account.
5. Click Test on the account.

The test should succeed. MailGester opens the Inbox and the spam folder read only. It does not delete, move or mark any mail.

Google Workspace accounts may have app passwords disabled by an administrator. If the app password option is missing, that is the usual reason.

## 6. Outlook accounts

Personal Outlook, Hotmail, Live and MSN accounts no longer accept IMAP with an app password. Microsoft switched basic authentication off for them in September 2024. MailGester therefore reads them through Microsoft Graph, which needs a free app registration in Microsoft Entra. You create it once and all your Outlook accounts share it.

### 6.1 You need an Entra directory

An app registration lives in a directory (tenant). If you already have a work or school account, or an existing Azure or Microsoft 365 tenant, you can use that. Otherwise create your own:

1. Open `azure.microsoft.com/free` in a new private window.
2. Choose the sign up option, not just sign in. Signing in with a personal account that has no directory leads to the error described in [Troubleshooting](#10-troubleshooting).
3. Finish the sign up. It creates a Default Directory that you own.

The app registration itself has no charge. The free account has credit and a time limit, and when they end the subscription is disabled until you choose to upgrade. See [Avoiding charges](#avoiding-charges).

### 6.2 Register the app

1. Open `entra.microsoft.com` and check in the top right that you are in your own directory.
2. Open App registrations, then New registration.
3. Name it `MailGester`.
4. For supported account types, choose **Any Entra ID Tenant + Personal Microsoft accounts**. On older screens it reads "Accounts in any organizational directory and personal Microsoft accounts".
5. Leave Redirect URI empty. Click Register.

### 6.3 Allow the device code sign in

1. Open Authentication (it may be labelled Authentication (Preview)).
2. Open the Settings tab.
3. Set **Allow public client flows** to Yes and save.

Do not add a redirect URI. The device code flow does not use one.

### 6.4 Add the mail permission

1. Open API permissions, then Add a permission.
2. Choose Microsoft Graph, then **Delegated permissions**.
3. Search for `Mail.Read`, tick only that one and click Add permissions.

Do not add any ReadWrite permission. MailGester only reads mail. You do not need to click Grant admin consent. Each account approves the permission when it signs in.

### 6.5 Copy the client ID

Open the Overview page of the app and copy the **Application (client) ID**.

### 6.6 In MailGester

1. Open Settings, Outlook sign in.
2. Paste the client ID.
3. Leave the authority as `https://login.microsoftonline.com/consumers`, which is correct for personal accounts. If any of your accounts is a work or school account, use `https://login.microsoftonline.com/common` and expect that its administrator may need to approve the app.
4. Click Save settings.
5. Open Accounts, choose the Outlook type and add the email address.
6. Click Sign in on the account. MailGester shows a code and a link.
7. Open the link (`microsoft.com/devicelogin`) in a browser, enter the code, choose the matching account and accept the permission.
8. Click Test on the account.

Repeat steps 5 to 8 for each Outlook account. Choose the matching account in the browser each time, since the browser may offer a different one by default.

If sign in expires after a long time without use, the account shows Sign in again. Click it and repeat the code step.

### Avoiding charges

The app registration is free according to a Microsoft moderator answer on Microsoft Q&A. The Azure free account is separate:

- When the free credit or the 30 days end, Microsoft's billing documentation says the subscription is disabled until you upgrade. Charges start only after you upgrade.
- Never click the option to upgrade to pay as you go.
- Do not create any Azure resources. You only need the directory and the app registration.
- In the portal, open All services, Subscriptions, select the subscription and check the free services grid for the expiry dates.
- You can cancel the subscription from its Overview page once the app registration works. Whether the directory and the app registration keep working after that is not confirmed, so check that the sign in still works before you rely on it.
- Watch for emails from Microsoft about credit and check your card statement after the first month.

## 7. Tell the AI what matters

Settings, About you and what matters is passed to the model with every digest. Keep it short and concrete. Example:

```
I am a software developer looking for a new job.

Ping me when a mail is about a job opportunity that fits my background. That means recruiters, headhunters, interview invitations, replies to my applications and job offers. Surface these even if they are in junk.

Do not ping for generic job board digests, newsletters or mass mailings that have no connection to my profile.
```

Other settings that matter:

| Setting | What it does |
| --- | --- |
| Run at, Time zone | When the daily digest runs. |
| First run lookback (hours) | How far back the first run reads. |
| Max catch up after downtime (days) | If the PC was off, the next start covers the missed time, up to this many days. |
| Keep history (days) | How long past digests are kept. |
| Language of the digest | The language the summary is written in. |
| Snippet length, Max mails per folder | How much of each mail the model sees. Larger values give better judgment and slower runs. |
| Priority keywords (one per line) | Matched in code, independent of the model. With the junk option ticked, a hit in a junk folder pings even if the model stayed quiet. |

## 8. First run

1. Open the Dashboard.
2. Check that the Ollama card shows a working connection, and that Test succeeded on every account in Accounts.
3. Click Run now.
4. Wait for the run to finish. A first run over many mails can take several minutes.
5. Check Discord for the post, and History in MailGester for the stored digest.

Read the first digest critically. If it misses mail that matters, adjust the text in About you and what matters. If the pings are too frequent, tighten it.

## 9. Daily use

- The digest runs at the time you set. The PC and Docker must be running. If the PC was off at that time, the digest runs when the app next starts.
- Back up the `app-data` volume. It holds your settings, history and the encryption keys for stored secrets. If you lose the keys, the saved passwords cannot be read and you must enter them again.
- Update with `git pull` followed by `docker compose up -d --build`.
- Stop with `docker compose down`. Add `-v` only if you want to delete all data and downloaded models.

## 10. Troubleshooting

**The page does not open.** Run `docker compose ps` and `docker compose logs app`. Check that nothing else uses the port.

**The port is already allocated.** Another program uses 8080. Change the port as described in part 1.

**Test ping fails.** Check that the webhook URL is complete and was not deleted in Discord.

**Ollama check fails.** Check that the `ollama` container is running and that the address is `http://ollama:11434`. If you use an Ollama on another machine, check that it listens on the network and that no firewall blocks port 11434.

**The model runs slowly.** It probably does not fit in video memory. Lower the Context size or choose a smaller model.

**No GPU is used.** Check that the NVIDIA Container Toolkit is installed and that `docker compose logs ollama` shows a GPU. Without one, Ollama runs on the CPU.

**Gmail test fails.** Check that you used an app password, not the account password, and that 2 Step Verification is on. If the connection is refused, check IMAP in Gmail settings under Forwarding and POP/IMAP.

**Outlook sign in code expires.** Click Sign in again and complete the browser step faster.

**Error AADSTS16000 or a message that your account does not exist in tenant "Microsoft Services".** This comes from the Azure portal, not from MailGester. A personal Microsoft account that only logged in has no directory of its own. Create one by signing up at `azure.microsoft.com/free` in a private window (part 6.1), then use `entra.microsoft.com` while the top right shows your own directory.

**Outlook sign in fails with an error about the application.** Check that Allow public client flows is Yes, that the account type allows personal accounts and that `Mail.Read` is added. If the error text says the application is not found, re-copy the client ID from the Overview page.

**An account shows an error in the digest.** MailGester reports a mailbox it cannot read in the post and pings, so you notice. Open Accounts, click Test and read the message.

**I forgot the admin password.** The UI has no reset, because the password is stored only as a hash in the database. If you are locked out, remove the stored password and set a new one on the next visit. This route has not been tested, so make a backup copy first. It needs the `sqlite3` tool on your PC:

```
docker compose stop app
docker compose cp app:/data/mailgester.db ./mailgester.db
sqlite3 mailgester.db "DELETE FROM kv WHERE key = 'admin';"
docker compose cp ./mailgester.db app:/data/mailgester.db
docker compose start app
```

Your settings, accounts and history stay intact. The simplest alternative is to remove the `app-data` volume, which also deletes all settings and history.
