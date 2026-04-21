# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/HelpDeskHero.Shared/HelpDeskHero.Shared.csproj src/HelpDeskHero.Shared/
COPY src/HelpDeskHero.UI/HelpDeskHero.UI.csproj src/HelpDeskHero.UI/
RUN dotnet restore src/HelpDeskHero.UI/HelpDeskHero.UI.csproj

COPY src/HelpDeskHero.Shared/ src/HelpDeskHero.Shared/
COPY src/HelpDeskHero.UI/ src/HelpDeskHero.UI/

RUN dotnet publish src/HelpDeskHero.UI/HelpDeskHero.UI.csproj -c Release -o /app/publish --no-restore

FROM nginx:alpine AS final
COPY docker/ui/nginx.conf /etc/nginx/conf.d/default.conf
COPY docker/ui/docker-entrypoint.sh /docker-entrypoint.sh
RUN chmod +x /docker-entrypoint.sh

COPY --from=build /app/publish/wwwroot /usr/share/nginx/html

EXPOSE 80
ENTRYPOINT ["/docker-entrypoint.sh"]
