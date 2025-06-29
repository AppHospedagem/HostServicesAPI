# Use uma imagem SDK do .NET para construir sua aplicação
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copie o arquivo de projeto e restaure as dependências
COPY *.csproj ./
RUN dotnet restore

# Copie todo o restante do código
COPY . ./

# Publique a aplicação em modo Release
RUN dotnet publish -c Release -o out

# Use uma imagem de runtime do .NET para rodar sua aplicação
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/out .

# Exponha a porta que sua aplicação ASP.NET Core usa (padrão é 80 ou 443)
EXPOSE 80
EXPOSE 443

# Defina o ponto de entrada da aplicação
ENTRYPOINT ["dotnet", "AppHospedagemAPI.dll"]
# Substitua "AppHospedagemAPI.dll" pelo nome do seu arquivo .dll principal.
# Se o seu projeto é MyApi.csproj, então será MyApi.dll