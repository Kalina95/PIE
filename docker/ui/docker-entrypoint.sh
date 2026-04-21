#!/bin/sh
set -e
# Adres API widziany z przeglądarki (host + port z mapowania compose).
API_URL="${API_PUBLIC_URL:-http://localhost:5000}"
# Trim trailing slash
API_URL="${API_URL%/}"

printf '{"Api":{"BaseUrl":"%s"}}' "$API_URL" > /usr/share/nginx/html/appsettings.json

exec nginx -g 'daemon off;'
