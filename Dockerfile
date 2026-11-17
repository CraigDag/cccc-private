# Built from the repository root: docker build -t cccc-next .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/ src/
RUN dotnet restore src/Cccc.Web/Cccc.Web.csproj
# Warnings are errors: a warning that only appears on a Linux build is one nobody would otherwise see.
RUN dotnet publish src/Cccc.Web/Cccc.Web.csproj -c Release -o /app --no-restore -warnaserror

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# The school's own times and its Danish name order need nothing installed: the image already carries
# the time-zone database and ICU.
# The image already listens on 8080 (ASPNETCORE_HTTP_PORTS).
ENV TZ=Europe/Copenhagen
EXPOSE 8080

# The store MUST be on a mounted folder (-v /srv/cccc-next:/data). The app refuses an empty or missing
# folder unless the first start says Store__Create=true, so a volume that went away is never silently
# replaced by an empty timetable. There is deliberately no VOLUME line: an anonymous volume would be
# one nobody named and nobody backs up.
ENV Store__Path=/data/store

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s CMD ["dotnet", "Cccc.Web.dll", "health"]
USER app
ENTRYPOINT ["dotnet", "Cccc.Web.dll"]
