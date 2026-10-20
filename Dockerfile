FROM node:22-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json* ./
RUN npm install --no-audit --no-fund
COPY web/ .
RUN npx ng build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS server
WORKDIR /src
COPY server/MailGester.csproj .
RUN dotnet restore
COPY server/ .
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=server /app .
COPY --from=web /web/dist/web/browser ./wwwroot
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DataDirectory=/data
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "MailGester.dll"]
