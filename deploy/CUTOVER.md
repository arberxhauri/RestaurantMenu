# Cutover: Render → Hetzner (myquickmenu.com)

Server: `hz` = 138.199.131.201 / 2a01:4f8:c010:8c3b::1, everything in `/srv`.
DNS: Squarespace (nameservers `ns-cloud-e*.googledomains.com`). Render app host: `restaurantmenu-7fzm.onrender.com`.

**Never touch** the MX records (Google Workspace mail) or the TXT records (site verification).

---

## A. Before the day

- [ ] **Now:** in Squarespace, delete the early Hetzner records on `@` (the A record `138.199.131.201` and the AAAA record
      `2a01:4f8:c010:8c3b::1`). While Caddy isn't running they send part of the visitors to a closed port; at the cutover
      they come back (step B5).
- [ ] **Lower the TTL** of `@`, `www` and `*` to 300 s if Squarespace offers a TTL field. Today they are 14399 s (4 h);
      wait at least that long after lowering before the cutover. If Squarespace has no TTL field, plan for ~4 h in which
      some visitors still reach Render (see B9).
- [ ] Stop the Hetzner app until the cutover, so its background jobs (emails, booking reminders, billing) don't run
      next to Render's: `ssh hz 'cd /srv && docker compose stop mqm'`
- [ ] Pick a quiet time (few orders/bookings), ~45 min plus DNS time.
- [ ] Keep `~/Downloads/RestaurantMenu.env` until the end (the final database copy reads Render's credentials from it).

## B. Cutover

1. **Stop the Hetzner app** (if it is running): `ssh hz 'cd /srv && docker compose stop mqm'`

2. **Final database copy** (Claude runs this; it replaces the server's database, so say "cutover" to confirm):
   - dump Render with `pg_dump` 18 into `/srv/backups/pg/render-final.dump` (check: not empty, 43 tables)
   - `DROP DATABASE mqm WITH (FORCE)` + `CREATE DATABASE mqm OWNER mqm` in mqm-db
   - `pg_restore --no-owner --no-acl --single-transaction --exit-on-error`
   - row counts of all tables compared with Render (must be identical)

3. **Final uploads copy** (Mac terminal; overwrites, adds new files):
   ```bash
   ssh -o UpdateHostKeys=no srv-d5p7qq49c44c73893umg@ssh.frankfurt.render.com 'tar -C /var/data -czf - images' \
   | ssh hz 'cd /srv && docker compose run --rm --no-deps -T --user root --entrypoint tar mqm -C /var/data -xzf - \
     && docker compose run --rm --no-deps --user root --entrypoint chown mqm -R 1654:1654 /var/data/images \
     && docker compose run --rm --no-deps --entrypoint sh mqm -c "du -sh /var/data/images; find /var/data/images -type f | wc -l"'
   ```
   The file count must match `ssh … 'find /var/data/images -type f | wc -l'` on Render.

4. **Start the app** (not Caddy yet) and check it: `ssh hz 'cd /srv && docker compose up -d mqm && sleep 15 && docker compose logs --tail=200 mqm | grep -E "schema is up to date|Email:|fail:|crit:"'`
   Expect "schema is up to date", "Email: sending through Resend", no `fail:`/`crit:`.

5. **DNS in Squarespace** (only these three names):

   | Name | Delete | Add |
   |---|---|---|
   | `@` | A `216.24.57.1`, `216.24.57.2`, `216.24.57.3`, `216.24.57.4` (Render) | A `138.199.131.201`, AAAA `2a01:4f8:c010:8c3b::1` |
   | `www` | CNAME `restaurantmenu-7fzm.onrender.com` | A `138.199.131.201`, AAAA `2a01:4f8:c010:8c3b::1` |
   | `*` | CNAME `restaurantmenu-7fzm.onrender.com` | A `138.199.131.201`, AAAA `2a01:4f8:c010:8c3b::1` |
   | `tirana` | CNAME `restaurantmenu-7fzm.onrender.com` | A `138.199.131.201`, AAAA `2a01:4f8:c010:8c3b::1` |

   **Keep** `rsend`, `send` (CNAME) and `resend._domainkey` (TXT): Resend's sending records. **Keep for now**
   `_acme-challenge` and `_cf-custom-hostname` (Render's certificates; harmless for Caddy, which doesn't use DNS
   challenges); delete them when Render is suspended (C). Check what the nameservers now
   answer (no Render addresses may be left):
   ```bash
   for n in myquickmenu.com www.myquickmenu.com tirana.myquickmenu.com; do echo "$n: $(dig +short A $n @ns-cloud-e1.googledomains.com) $(dig +short AAAA $n @ns-cloud-e1.googledomains.com)"; done
   ```

6. **Start Caddy** and watch the certificates: `ssh hz 'cd /srv && docker compose up -d && docker compose logs -f caddy'`
   Wait for `certificate obtained successfully` for `myquickmenu.com` and `www.myquickmenu.com` (Ctrl+C to stop watching).
   If it failed while DNS still pointed at Render, retry right away with `ssh hz 'cd /srv && docker compose restart caddy'`.

7. **Check from outside** (Mac):
   ```bash
   curl -sI https://myquickmenu.com | head -1                         # HTTP/2 200
   curl -sI https://www.myquickmenu.com | grep -iE "^HTTP|^location"   # 301 → https://myquickmenu.com/
   curl -sI http://myquickmenu.com | grep -iE "^HTTP|^location"        # 308 → https://
   curl -sI https://tirana.myquickmenu.com | head -1                  # 200 (certificate on first visit, a few s)
   curl -sI https://olivers-italian.myquickmenu.com/menu | head -1     # 200
   ```
   In the browser: sign in (everyone signs in again once), a menu, a booking page, the kitchen display, an upload.

8. **Paddle**: the webhook URL (`https://myquickmenu.com/billing/webhooks/paddle`) doesn't change. In Paddle → Developer
   tools → Notifications, check that deliveries after the switch succeed.

9. **Writes that reached Render after the dump** (while DNS propagates): a few hours after step 5, Claude compares the
   newest rows on Render and Hetzner (Orders, Reservations, Feedback, AspNetUsers, Invoices); anything newer on Render is
   re-entered by hand.

## C. Afterwards

- [ ] Keep Render running 1–2 days as a fallback, but stop its duplicate emails (booking reminders, billing mails go
      out from both otherwise): in Render → Environment, remove `Email__ResendApiKey` (Render then logs "Email: off").
- [ ] Push the code (`main`) only after the switch; Render redeploys it and keeps working (nothing Render-specific changed).
- [ ] After 1–2 days without problems: suspend the Render web service and database (Render dashboard), then delete
      the Squarespace records `_acme-challenge` and `_cf-custom-hostname` (both point to `…renderdns.com`).
- [ ] Delete leftovers with secrets: `rm ~/Downloads/RestaurantMenu.env`; on the server `rm /srv/.env.bak-* /srv/mqm.env.bak-*`.
- [ ] The nightly backup (03:00) writes `/srv/backups/pg/mqm-*.dump`; check `/srv/backups/pg/backup.log` the next morning.
      Uploads aren't in it: keep Hetzner's server backups on, or copy the `mqm_mqm_data` volume elsewhere now and then.

## Rollback

If something is badly wrong and can't be fixed quickly:
1. Squarespace: put back `@` A `216.24.57.1–4`, `www`, `*` and `tirana` CNAME `restaurantmenu-7fzm.onrender.com`; delete the
   Hetzner A/AAAA records (and restore `Email__ResendApiKey` on Render if you removed it).
2. `ssh hz 'cd /srv && docker compose stop caddy mqm'`
3. Anything written on Hetzner in the meantime exists only there (Claude can list it).
