# Smartnet API — ASP.NET Core on .NET 10, Linux
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Smartnet.slnx ./
COPY Smartnet.Api/Smartnet.Api.csproj              Smartnet.Api/
COPY Smartnet.Domain/Smartnet.Domain.csproj        Smartnet.Domain/
COPY Smartnet.Infrastructure/Smartnet.Infrastructure.csproj Smartnet.Infrastructure/
COPY Smartnet.Tests/Smartnet.Tests.csproj          Smartnet.Tests/
RUN dotnet restore Smartnet.Api/Smartnet.Api.csproj

COPY . .
RUN dotnet publish Smartnet.Api/Smartnet.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl is used by the compose healthcheck.
#
# mariadb-client provides mysqldump and mysql, which the backup feature shells out to. It is deliberately
# not re-implemented in C#: a correct logical dump is column types, character sets, escaping of binary
# blobs and the exact CREATE TABLE the server would emit, and mysqldump is the reference implementation
# of that. A hand-rolled one would be a second, worse implementation whose bugs surface on the day the
# backup is needed. See Smartnet.Infrastructure/Backups/MySqlDatabaseBackup.cs.
#
# Both installed as root, before the USER switch below.
RUN apt-get update && apt-get install -y --no-install-recommends curl mariadb-client \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

# Don't run as root.
#
# There is deliberately no useradd here. The .NET 8+ runtime images already ship a non-root user
# named "app", so creating one fails with "username already in use" — useradd exit code 9, which is
# exactly how the first deploy broke. APP_UID is set by the base image to that user's id, and is
# Microsoft's documented way to select it.
#
# This comes AFTER the COPY on purpose. Switching user first means the copy runs unprivileged into a
# root-owned /app: either it fails outright, or it succeeds and leaves files the app cannot read.
USER $APP_UID

EXPOSE 5080
ENTRYPOINT ["dotnet", "Smartnet.Api.dll"]
