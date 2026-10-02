FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY OrderManagementApi.csproj ./

RUN dotnet restore OrderManagementApi.csproj

COPY . .

RUN dotnet publish OrderManagementApi.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "OrderManagementApi.dll"]