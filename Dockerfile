# One image recipe for every .NET service: docker build --build-arg PROJECT=src/Services/Catalog/Shop.Catalog --build-arg APP=Shop.Catalog .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish "$PROJECT" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ARG APP
WORKDIR /app
ENV APP_DLL=${APP}.dll \
    ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["sh", "-c", "exec dotnet \"/app/$APP_DLL\""]
