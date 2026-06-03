FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["RMIA.sln", "./"]
COPY ["RmaWorker/RmaWorker.csproj", "RmaWorker/"]
RUN dotnet restore "RmaWorker/RmaWorker.csproj"
COPY . .
RUN dotnet publish "RmaWorker/RmaWorker.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM runtime AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "RmaWorker.dll"]
