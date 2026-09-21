# Samwise.Infrastructure.WebPush

Web Push adapter for Samwise work-item notifications, built on [Lib.Net.Http.WebPush](https://github.com/tpeczek/Lib.Net.Http.WebPush) (VAPID, `aes128gcm`).

- `Keys/VapidKeyInitializer` creates this instance's P-256 VAPID key pair once, into the `PushApplicationKeys` document. It never regenerates the pair, because that would invalidate every browser subscription.
- `Keys/VapidKeyProtector` protects the private key with ASP.NET Data Protection.
- `Sending/WebPushNotificationSender` implements `IPushNotificationSender` and `IPushApplicationKeyProvider`:
  - sends a JSON payload `{title, body, url, tag}` with a 24-hour TTL and high urgency for approvals and questions
  - reports 404/410 as an expired subscription.
- `WebPushOptions.Subject` (`WebPush:Subject`) is the VAPID contact. It defaults to `mailto:` + `OwnerAccount:Email`, and sending fails clearly when neither is set, because Apple rejects pushes without a subject.

Which events notify, device bookkeeping and pruning live in `Samwise.Logic/Notifications`. The browser side (`sw.js`, `js/push.js`, the Notifications page) lives in `Samwise.Web`.
