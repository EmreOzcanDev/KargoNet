FROM mcr.microsoft.com/dotnet/sdk:10.0 AS sdk
WORKDIR /src
COPY KargoNet.sln ./
COPY Directory.Build.props ./
COPY src/ src/
RUN dotnet restore src/Web/Portal.Web
RUN dotnet publish src/Web/Portal.Web -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=sdk /app/publish .
USER app
EXPOSE 5000
ENTRYPOINT ["dotnet", "/app/Portal.Web.dll"]
