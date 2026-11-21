# Histórico de versões

## [1.2.0] - 2026-11-21

### Aplicativo

- Adicionado gráfico do painel com amostras reais de CPU e métricas atuais de RAM e tráfego de rede.
- O scanner YARA passou a ler arquivos em blocos, detectar padrões que atravessam blocos e analisar arquivos menores que 500 MiB sem carregar o arquivo inteiro na memória.
- Regras YARA incluem a assinatura completa do EICAR e padrões para notas de resgate, incluindo `All your files are encrypted`.
- O Modo Jogo tenta ativar o plano Alto Desempenho quando disponível e restaura o plano anterior ao desativar ou encerrar o aplicativo. Se o plano não estiver disponível, o tema visual continua funcionando e o app informa a limitação.
- Adicionado manifesto para solicitar elevação pelo UAC e habilitar DPI por monitor no Windows 10/11. Execute apenas uma cópia confiável, pois o aplicativo passa a rodar como administrador.
- Testes xUnit validam EICAR em memória e notas de resgate em arquivo temporário removido após o teste.

### Repositório e validação

- Adicionada licença MIT, configuração `.editorconfig` e regras `.gitattributes`.
- Adicionado workflow do GitHub Actions em Windows para restaurar, compilar e testar com .NET 11.
- A compilação Release foi validada sem avisos e a suíte xUnit passou.
