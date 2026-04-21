# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/HelpDeskHero.Shared/HelpDeskHero.Shared.csproj src/HelpDeskHero.Shared/
COPY src/HelpDeskHero.Api/HelpDeskHero.Api.csproj src/HelpDeskHero.Api/
RUN dotnet restore src/HelpDeskHero.Api/HelpDeskHero.Api.csproj

COPY src/HelpDeskHero.Shared/ src/HelpDeskHero.Shared/
COPY src/HelpDeskHero.Api/ src/HelpDeskHero.Api/

RUN dotnet publish src/HelpDeskHero.Api/HelpDeskHero.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN mkdir -p /app/storage
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "HelpDeskHero.Api.dll"]
