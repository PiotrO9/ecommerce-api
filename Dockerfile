FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src
COPY E-commerce-backend.csproj ./
RUN dotnet restore E-commerce-backend.csproj

COPY . .
RUN dotnet publish E-commerce-backend.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app
COPY --from=build /app/publish .

RUN mkdir -p /app/keys && chown $APP_UID:$APP_UID /app/keys

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["dotnet", "E-commerce-backend.dll"]
