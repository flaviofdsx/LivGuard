# LivGuard

# Instalador do LivGuard

O instalador é compilado com Inno Setup 6 e publicado como aplicativo Windows x64
autocontido em .NET 11. A máquina de destino não precisa instalar o runtime do .NET.

## Compilar

1. Instale o .NET 11 SDK e o Inno Setup 6.
2. Na raiz do projeto, execute:

   ```powershell
   .\installer\build-installer.ps1
   ```

   Se o compilador não estiver no caminho padrão:

   ```powershell
   .\installer\build-installer.ps1 -InnoSetupCompiler "C:\caminho\ISCC.exe"
   ```

O executável de instalação e seu checksum SHA-256 serão criados em
`artifacts\installer`. Os arquivos publicados ficam em `artifacts\publish`.
O checksum detecta corrupção do arquivo; não substitui uma assinatura digital
nem autentica quem o publicou.

## O que o instalador faz

- Instala somente para o usuário atual em `%LOCALAPPDATA%\Programs\LivGuard`.
- Mostra um assistente em português com informações antes da instalação.
- Cria um atalho no Menu Iniciar; o atalho na Área de Trabalho é opcional e
  desmarcado por padrão.
- Não pede elevação, não registra serviço, não cria tarefa agendada, não configura
  inicialização automática, exclusões do antivírus ou regras de firewall.
- Não abre o aplicativo automaticamente após instalar.
- Preserva os dados do usuário ao desinstalar. Dados locais ficam em
  `%LOCALAPPDATA%\LivGuard` e devem ser removidos manualmente se desejado.

O instalador não é assinado digitalmente. Uma distribuição pública deve ser assinada
com um certificado de assinatura de código emitido para o publicador; não use
certificados autoassinados para contornar o SmartScreen.

## Antes de distribuir publicamente

O projeto acompanha o .NET 11 e os pacotes Microsoft.Extensions/Microsoft.Data.Sqlite
11.0.0. A assinatura digital exige um certificado válido do publicador; o checksum
SHA-256 sozinho não substitui essa assinatura.
