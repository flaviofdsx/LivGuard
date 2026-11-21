# Histórico de versões

## [Não lançado]

### Aplicativo

- Janela sem bordas agora pode ser redimensionada, com moldura adaptável e tamanho mínimo.
- Adicionados toasts animados para confirmar ações e apresentar erros sem diálogos modais desnecessários.
- Cartões de hardware exibem um indicador animado durante o carregamento assíncrono; resultados de uma tela já fechada não são atualizados.
- Web Shield compara domínios e subdomínios com limites de host, sem alterar o proxy do sistema ou instalar certificados.
- System Integrity monitora, somente para leitura, alterações nas chaves Run/RunOnce de HKLM e HKCU e registra inclusões, alterações e remoções.

## [1.2.0] - 2026-11-21

### Aplicativo

- Adicionado gráfico do painel com amostras reais de CPU e métricas atuais de RAM e tráfego de rede.
- O scanner passou a usar o mecanismo nativo libyara e analisar arquivos menores que 500 MiB sem carregar o arquivo inteiro na memória.
- Regras YARA incluem a assinatura completa do EICAR e padrões para notas de resgate, incluindo `All your files are encrypted`.
- O Modo Jogo tenta ativar o plano Alto Desempenho quando disponível e restaura o plano anterior ao desativar ou encerrar o aplicativo. Se o plano não estiver disponível, o tema visual continua funcionando e o app informa a limitação.
- Adicionado manifesto para solicitar elevação pelo UAC e habilitar DPI por monitor no Windows 10/11. Execute apenas uma cópia confiável, pois o aplicativo passa a rodar como administrador.
- Integrado o mecanismo nativo libyara às regras locais; adicionado analisador de cabeçalhos e importações PE, cujos indicadores são heurísticos e não acionam quarentena automática.
- Criado o atualizador MalwareBazaar por POST autenticado para importar somente hashes SHA-256 recentes ao banco local. Não baixa amostras; requer `MALWAREBAZAAR_AUTH_KEY`.
- Testes xUnit validam EICAR em memória e notas de resgate em arquivo temporário removido após o teste.

### Repositório e validação

- Adicionada licença MIT, configuração `.editorconfig` e regras `.gitattributes`.
- Adicionado workflow do GitHub Actions em Windows para restaurar, compilar e testar com .NET 11.
- A compilação Release foi validada sem avisos e a suíte xUnit passou.
