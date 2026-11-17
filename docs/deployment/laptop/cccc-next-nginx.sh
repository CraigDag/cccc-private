#!/bin/bash
# Lets nginx serve the rebuild on port 8443, beside the current app, or stops it again
# (docs/deployment/trying-it-beside-the-current-app.md, step 9, which is optional and Craig's decision).
#
#   cccc-next-nginx.sh on    install the rebuild's site, with the current app's own certificate
#   cccc-next-nginx.sh off   take the rebuild's site away again
#
# The current app's site (/etc/nginx/sites-available/cccc-scheduler) is only READ, for where its
# certificate is; nothing in it changes. nginx checks the whole configuration before it takes any of
# this, and if it refuses, the rebuild's site is taken straight back out: a site nginx refuses would
# otherwise stop nginx, and with it the current app, at the next restart.
set -uo pipefail

CURRENT=/etc/nginx/sites-available/cccc-scheduler
SITE=/etc/nginx/sites-available/cccc-next
LINK=/etc/nginx/sites-enabled/cccc-next
TEMPLATE="$(dirname "$(readlink -f "$0")")/cccc-next.nginx"

case "${1:-}" in
on)
  [ -f "$TEMPLATE" ] || { echo "REFUSED: $TEMPLATE is not there. Nothing changed."; exit 1; }
  crt=$(sed -n 's/^[[:space:]]*ssl_certificate[[:space:]]\+\([^;]*\);.*/\1/p' "$CURRENT" 2>/dev/null | head -1)
  key=$(sed -n 's/^[[:space:]]*ssl_certificate_key[[:space:]]\+\([^;]*\);.*/\1/p' "$CURRENT" 2>/dev/null | head -1)
  if [ -z "$crt" ] || [ -z "$key" ]; then
    echo "REFUSED: could not find the certificate in $CURRENT, so the rebuild has none to use. Nothing changed."
    exit 1
  fi
  sed -e "s|/etc/ssl/cccc/server.crt|$crt|" -e "s|/etc/ssl/cccc/server.key|$key|" "$TEMPLATE" > "$SITE.part" && mv -f "$SITE.part" "$SITE"
  ln -sf "$SITE" "$LINK"
  if said=$(nginx -t 2>&1); then
    systemctl reload nginx && echo "ON: nginx serves the rebuild on port 8443, with the certificate $crt."
  else
    rm -f "$LINK"
    echo "REFUSED: nginx would not take the rebuild's site, so it was taken out again and nothing changed."
    echo "What nginx said:"; echo "$said"
    exit 1
  fi
  ;;
off)
  rm -f "$LINK" "$SITE"
  if nginx -t 2>/dev/null; then systemctl reload nginx; fi
  echo "OFF: nginx no longer serves the rebuild."
  ;;
*)
  echo "Usage: cccc-next-nginx.sh on | off"
  exit 2
  ;;
esac
