# Imagen de OpenMU con nuestros cambios: el parche del nucleo (custom/patches) y los plugins MuCustom.
# Contexto: server/ (ver docker-compose.local.yml). Basado en OpenMU/src/Startup/Dockerfile.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS base
WORKDIR /app

# krb5-libs: Npgsql la busca al conectar (sin ella escribe un error de libgssapi en el log).
RUN apk add --no-cache icu-libs krb5-libs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

EXPOSE 8080 44405 45901 45902 45903 45904 45905 45906 45980

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
RUN apk add --no-cache git
WORKDIR /build

# Misma estructura que en el repo: OpenMU/src y custom/ uno al lado del otro.
COPY OpenMU/src OpenMU/src
COPY custom custom

# El parche puede venir ya aplicado (si el submodulo local lo tiene); si no, se aplica aqui.
# Fuentes y parche pasan a LF antes, asi no importa con que fin de linea se hizo el checkout.
RUN find OpenMU/src -type f \( -name '*.cs' -o -name '*.csproj' -o -name '*.props' \) -exec sed -i 's/\r$//' {} + \
    && sed 's/\r$//' custom/patches/openmu-core.patch > /tmp/openmu-core.patch \
    && cd OpenMU \
    && if git apply --check -p1 /tmp/openmu-core.patch 2>/dev/null; then \
           git apply -p1 --whitespace=nowarn /tmp/openmu-core.patch && echo "openmu-core.patch aplicado"; \
       else \
           git apply --reverse --check -p1 /tmp/openmu-core.patch && echo "openmu-core.patch ya estaba aplicado"; \
       fi

WORKDIR /build/OpenMU/src/Startup
RUN dotnet publish "MUnique.OpenMU.Startup.csproj" -c Release -o /app/publish -p:ci=true

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .

RUN mkdir -p /app/logs /app/data-protection-keys && chmod 777 /app/logs /app/data-protection-keys

ARG APP_UID=1000
USER ${APP_UID}

ENTRYPOINT ["dotnet", "MUnique.OpenMU.Startup.dll", "-autostart"]
