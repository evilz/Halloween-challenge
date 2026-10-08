FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json Directory.Build.props ./
COPY src/Halloween.Engine/ src/Halloween.Engine/
COPY src/Halloween.Web/ src/Halloween.Web/
RUN dotnet publish src/Halloween.Web/Halloween.Web.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
RUN mkdir -p /app/App_Data && chown -R $APP_UID /app/App_Data
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Halloween.Web.dll"]
