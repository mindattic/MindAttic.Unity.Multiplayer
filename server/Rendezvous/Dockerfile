# The rendezvous service image. Build from the repository root (the server compiles Core's
# join-code format from the Unity package):
#
#   docker build -f server/Rendezvous/Dockerfile -t mindattic-rendezvous .
#   docker run -p 8080:8080 -e Rendezvous__RegistrationSecret=<secret> mindattic-rendezvous
#
# Plain HTTP on 8080 suits a reverse proxy that terminates TLS (set Rendezvous__UseForwardedHeaders=true
# and Rendezvous__KnownProxies__0=<proxy IP>). To serve TLS from Kestrel itself, mount a certificate and set
#   ASPNETCORE_HTTP_PORTS=  ASPNETCORE_HTTPS_PORTS=8443
#   ASPNETCORE_Kestrel__Certificates__Default__Path=/certs/rendezvous.pfx
#   ASPNETCORE_Kestrel__Certificates__Default__Password=<password>
#   Rendezvous__RequireHttps=true
#   RENDEZVOUS_HEALTH_URL=https://localhost:8443/healthz   (for the HEALTHCHECK)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY unity/com.mindattic.unity.multiplayer/Runtime/Core/ unity/com.mindattic.unity.multiplayer/Runtime/Core/
COPY tests/Core.Build/Core.Build.csproj tests/Core.Build/
COPY server/Rendezvous/Rendezvous.csproj server/Rendezvous/
RUN dotnet restore server/Rendezvous/Rendezvous.csproj
COPY server/Rendezvous/ server/Rendezvous/
RUN dotnet publish server/Rendezvous/Rendezvous.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD ["dotnet", "Unity.Multiplayer.Rendezvous.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "Unity.Multiplayer.Rendezvous.dll"]
