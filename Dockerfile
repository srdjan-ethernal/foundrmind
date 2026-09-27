FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Foundrmind.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    DISABLE_HTTPS_REDIRECT=1 \
    DATA_PROTECTION_PATH=/data/keys
EXPOSE 8080
ENTRYPOINT ["dotnet", "Foundrmind.dll"]
