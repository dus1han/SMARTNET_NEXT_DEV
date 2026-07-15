#!/usr/bin/env bash
# Regenerates the typed API client from the API's OpenAPI schema.
#
# Needs no database and no real secrets: the schema is derived from the compiled assembly, not from
# a running server. The dummy values below exist only because Program.cs validates its configuration
# at startup — deliberately, so that a missing signing key is a startup failure rather than a
# first-login failure.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
out="${1:-$root/packages/api-client}"

export DOTNET_ROLL_FORWARD=Major
export ConnectionStrings__Smartnet="Server=localhost;Database=smartnet_invsys_dev;User=schema-gen;Password=;"
export Jwt__SigningKey="schema-generation-only-never-used-to-sign-anything"
export Jwt__Issuer=smartnet
export Jwt__Audience=smartnet

dotnet build -c Release "$root/apps/api/Smartnet.Api/Smartnet.Api.csproj" --nologo -v quiet

swagger tofile --output "$out/openapi.json" \
  "$root/apps/api/Smartnet.Api/bin/Release/net10.0/Smartnet.Api.dll" v1 > /dev/null

npx --yes openapi-typescript "$out/openapi.json" -o "$out/schema.d.ts"
