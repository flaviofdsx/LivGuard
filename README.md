# 🛡️ LivGuard Antivirus

> *"É a proteção que criei para a minha filha e para o mundo."*  
> — **Flaviofdsx** (Criador)

O **LivGuard** é um antivírus open-source para Windows 10/11, construído em **C# (.NET 8)** e **WPF**, inspirado no visual e na performance da linha **Acer Predator**. O projeto foi desenvolvido para unir segurança de nível corporativo com uma interface gamer imersiva.

![Status](https://img.shields.io/badge/status-active-brightgreen)
![Versão](https://img.shields.io/badge/versão-v1.2.0-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![Licença](https://img.shields.io/badge/licença-MIT-green)

---

## ✨ Funcionalidades Principais

O LivGuard não é apenas um scanner; é uma suíte completa de segurança e monitoramento.

### 🛡️ Proteção em Tempo Real
- **File System Shield:** Monitoramento contínuo da pasta `Downloads` e diretórios críticos via `FileSystemWatcher`. Bloqueia e isola arquivos maliciosos automaticamente.
- **Web Shield & AdBlocker:** Bloqueio de domínios de phishing, rastreadores e anúncios indesejados.
- **System Integrity:** Monitora alterações suspeitas no Registro do Windows.
- **Auto-Updater:** Verificação silenciosa de novas assinaturas em background (a cada 24h).

### 🧠 Motores de Varredura
- **Varredura por Hash (SHA-256):** Comparação rápida com banco de dados SQLite.
- **Regras YARA:** Detecção heurística de malware (testado com EICAR).
- **Memory Scanner:** Varredura de processos em execução na RAM (detecção de fileless malware).
- **AI File Analyzer:** Heurística simulada por Machine Learning para identificar "droppers".

### 🎮 Interface & Usabilidade (Predator Edition)
- **Design Imersivo:** Janela sem bordas nativas, cantos chanfrados e tema escuro profundo.
- **Game Mode:** Ao ativar, o app muda para o tema *Vermelho Sangue*, toca um som e tenta acionar o modo Turbo do PredatorSense oficial.
- **Gráfico Fluido:** Monitor de atividade em tempo real (estilo eletrocardiograma) no painel principal.
- **Notificações Toast:** Avisos nativos do Windows 11 quando uma ameaça é bloqueada.

### 🛠️ Ferramentas Extras
- **Privacy Shield:** Monitor e bloqueador de processos que tentam acessar Webcam/Microfone.
- **Otimizador de PC:** Limpador de arquivos temporários e cache do Windows.
- **Radar de Rede (Wi-Fi):** Lista dispositivos conectados ao seu roteador e alerta sobre intrusos.
- **Proteção Infantil:** Bloqueio de sites adultos com senha (ideal para famílias).
- **Cofre (Vault):** Criptografia AES-256 para arquivos confidenciais.
- **Quarentena:** Isolamento seguro de arquivos infectados com opção de restauração.
- **Telegram Bot:** Envio de alertas de segurança direto no seu celular.

---

## 🏗️ Arquitetura do Projeto

O projeto segue os princípios de **Clean Architecture** e está dividido em módulos:

```text
LivGuard/
├── Flavirus.Core/       # Motor de varredura, YARA, Quarentena, Cofre, WMI.
├── Flavirus.UI/         # Interface WPF, Tema Predator, Tray Icon, Notificações.
├── Flavirus.Service/    # Windows Service (opcional) para proteção em background.
├── Flavirus.Updater/    # Atualizador de assinaturas.
└── Flavirus.Tests/      # Testes unitários (xUnit) - Ex: Detecção de EICAR.
