#!/bin/sh
# One broker user per application, allowed only what that application needs (README.md). Every call is a PUT, so
# running this again brings users and rights in line with this file. Run by the "users" service in
# docker-compose.yml; needs RABBITMQ_URL, ADMIN_USER, ADMIN_PASSWORD and each application's password.
set -eu

VHOST=%2F            # "/", URL-encoded
EXCHANGE=CleanArch   # the exchange every application shares (ExchangeName / exchangeName)

# Escapes backslashes for a JSON string.
json() {
    printf '%s' "$1" | sed 's/\\/\\\\/g'
}

management() {
    curl -fsS --retry 10 --retry-connrefused --retry-delay 2 -u "$ADMIN_USER:$ADMIN_PASSWORD" \
        -H 'content-type: application/json' -X PUT "$RABBITMQ_URL/api/$1" -d "$2" > /dev/null
}

# app <user> <password> <ClientName> <routing keys it may publish, as a regex>
#
# The application may use the shared exchange and the queues named after its ClientName (queues are named
# "<clientname>.<bus>[.<instance>]", lower case), and may publish only routing keys matching the regex.
# Legacy applications publish under the event's full type name. Use '^$' for an application that never publishes.
app() {
    : "${2:?no password for $1: set it in .env}"
    prefix=$(printf '%s' "$3" | tr 'A-Z' 'a-z' | sed 's/\./\\./g')
    own=$(json "^($EXCHANGE|$prefix\\..*)\$")

    management "users/$1" "{\"password\":\"$2\",\"tags\":\"\"}"
    management "permissions/$VHOST/$1" "{\"configure\":\"$own\",\"write\":\"$own\",\"read\":\"$own\"}"
    management "topic-permissions/$VHOST/$1" "{\"exchange\":\"$EXCHANGE\",\"write\":\"$(json "$4")\",\"read\":\".*\"}"
    echo "$1: ready"
}

#   user            password                  ClientName       may publish
app cleanarch-api   "${API_PASSWORD:-}"       CleanArch.Api    '^Equipment\.'
# app legacy-app1   "${LEGACY_APP1_PASSWORD:-}" LegacyApp1     '^LegacyApp1\.Events\.'
