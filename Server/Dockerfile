FROM mcr.microsoft.com/dotnet/sdk:10.0.400 AS build
WORKDIR /src

COPY JumpingNinja.Api/JumpingNinja.Api.csproj JumpingNinja.Api/
RUN dotnet restore JumpingNinja.Api/JumpingNinja.Api.csproj

COPY JumpingNinja.Api/ JumpingNinja.Api/
WORKDIR /src/JumpingNinja.Api
RUN dotnet publish JumpingNinja.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0.11 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /home/app/.aspnet/DataProtection-Keys \
    && chown -R app:app /home/app/.aspnet
COPY --from=build /app/publish .
USER app
ENTRYPOINT ["dotnet", "JumpingNinja.Api.dll"]
