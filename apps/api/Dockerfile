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

# curl is used by the compose healthcheck
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# don't run as root
RUN useradd -m -u 1001 app
USER app

COPY --from=build /app .
EXPOSE 5080
ENTRYPOINT ["dotnet", "Smartnet.Api.dll"]
