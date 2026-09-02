# Nyra — AI VTuber Assistant

> **Uma assistente de IA que vê, ouve, conversa, lembra e ajuda a programar.**

**Nyra** é um projeto experimental de assistente de inteligência artificial com aparência de **VTuber**, desenvolvido em **Unity + Live2D**, com backends em **Node.js** e **Python**, e integração com **Google Gemini** e **Azure Speech Services**.

O objetivo é criar uma companheira digital capaz de interagir naturalmente por **texto e voz**, compreender o contexto das conversas, analisar a tela do computador e auxiliar no desenvolvimento de software.

---

## Features

### Artificial Intelligence

* Conversação contextual
* Persona própria da Nyra
* Histórico de conversa
* Memória persistente
* Detecção de contexto emocional
* Modo técnico para programação
* Respostas em português brasileiro
* Sistema anti-repetição
* Integração com Google Gemini

### Voice Interaction

* Speech-to-Text contínuo
* Text-to-Speech neural
* Voz `pt-BR-YaraNeural`
* Controle de velocidade e pitch
* Detecção de silêncio
* Modo hands-free
* Filtro contra reconhecimento duplicado
* Proteção contra eco
* Pausa do STT enquanto a Nyra fala

### Computer Vision

A Nyra pode analisar o conteúdo da tela quando solicitada.

Exemplos:

> "Nyra, olha minha tela."

> "Veja esse erro."

> "Analisa esse código."

O sistema pode:

* Capturar screenshots
* Detectar texto selecionado
* Analisar código
* Identificar erros
* Identificar warnings
* Interpretar janelas e interfaces
* Enviar o conteúdo para análise multimodal pelo Gemini

### VTuber Avatar

* Avatar Live2D
* Lip Sync em tempo real
* Animações Idle
* Animações aleatórias
* Posicionamento dinâmico
* Integração entre áudio e expressão do avatar

---

# Architecture
```text
                         ┌─────────────────────────────┐
                         │       UNITY FRONTEND        │
                         │                             │
                         │  Live2D • Chat • Microfone  │
                         │  Lip Sync • Animações • UI  │
                         └──────────────┬──────────────┘
                                        │
                              HTTP POST /chat
                                        │
                                        ▼
                         ┌─────────────────────────────┐
                         │      NODE.JS BACKEND        │
                         │          :3000              │
                         │                             │
                         │  Gemini • Memória • Chat    │
                         │  Contexto • Visão • Lógica  │
                         └──────────────┬──────────────┘
                                        │
                         ┌──────────────┴──────────────┐
                         │                             │
                         ▼                             ▼
                ┌──────────────────┐          ┌──────────────────┐
                │   VISION AGENT   │          │  EMOTION SERVER  │
                │      Python      │          │    Flask :5000   │
                │                  │          │                  │
                │  Screenshot      │          │  BERTweet-PT     │
                │  Clipboard       │          │  Sentiment       │
                │  Gemini Vision   │          │  Classification  │
                └────────┬─────────┘          └────────┬─────────┘
                         │                             │
                         │                             │
                         └──────────────┬──────────────┘
                                        │
                                        ▼
                              ┌──────────────────┐
                              │     GEMINI API   │
                              └──────────────────┘


                         ┌─────────────────────────────┐
                         │       AZURE SPEECH         │
                         │                             │
                         │          STT + TTS          │
                         │      pt-BR-YaraNeural       │
                         └─────────────────────────────┘
```

---

# Components

## 1. Unity Frontend

O frontend é responsável pela interface, avatar, entrada de voz, reprodução de áudio e interação com o backend.

**Main Scene:**

```text
AIVTUBer2.unity
```

**Live2D Model:**

```text
L2DZeroVS
```

### Core Scripts

| Script                   | Responsibility                                              |
| ------------------------ | ----------------------------------------------------------- |
| `ChatManager.cs`         | Orquestra login, chat, voz, TTS e comunicação com o backend |
| `AzureSTTUnity.cs`       | Reconhecimento de voz via Azure Speech                      |
| `AzureTTS_Rest.cs`       | Síntese de voz neural                                       |
| `Live2DLipSync.cs`       | Sincronização labial                                        |
| `RandomIdleWithDelay.cs` | Gerenciamento de animações Idle                             |
| `MicrophoneCapture.cs`   | Captura hands-free e detecção de silêncio                   |
| `EmotionClient.cs`       | Comunicação com o Emotion Server                            |
| `UIController.cs`        | Controle da interface                                       |
| `GeminiAI.cs`            | Integração legada/alternativa com Gemini                    |
| `DialogflowRequest.cs`   | Integração legada com Dialogflow                            |

---

## 2. Node.js Backend

**Main File:**

```text
AI voice/Backend/server.js
```

**Server:**

```text
localhost:3000
```

O backend funciona como o **núcleo de processamento da Nyra**.

### Core Responsibilities

* Comunicação com Gemini
* Gerenciamento de contexto
* Histórico de conversa
* Memória persistente
* Detecção de contexto emocional
* Modo técnico
* Modo de visão
* Processamento das respostas
* Limpeza e normalização do texto
* Sistema anti-repetição

### Persistent Memory

A Nyra possui um sistema de memória persistente baseado em arquivo.

```text
memory.json
```

A memória permite manter informações relevantes entre diferentes sessões.

> **Nota:** dados pessoais e a memória real do usuário não devem ser enviados para o repositório público.

---

## 3. Vision Agent

**Main File:**

```text
AI voice/Emotion_AI/vision_agent.py
```

O Vision Agent é responsável pela capacidade da Nyra de **observar e interpretar a tela**.

### Processing Flow

```text
Usuário
   │
   ▼
"Nyra, olha a tela"
   │
   ▼
Node.js Backend
   │
   ▼
trigger.txt
   │
   ▼
Vision Agent
   │
   ├── Captura screenshot
   ├── Verifica clipboard
   ├── Processa imagem
   └── Envia para Gemini
   │
   ▼
visao.txt
   │
   ▼
Node.js Backend
   │
   ▼
Nyra responde
```

### Technologies

* Python
* PyAutoGUI
* PIL
* Clipboard
* Gemini multimodal

---

## 4. Emotion Server

**Main File:**

```text
AI voice/Emotion_AI/emotion_server.py
```

**Server:**

```text
localhost:5000
```

Utiliza **BERTweet-PT** para classificação de sentimento.

### API Endpoint

```text
POST /analyze
```

### Output Classes

```text
alegria
tristeza
neutra
```

O resultado inclui um score de confiança.

---

# Voice System

A Nyra utiliza **Azure Speech Services** para reconhecimento e síntese de voz.

## Speech-to-Text

```text
Microphone
    ↓
Azure Speech-to-Text
    ↓
Text
    ↓
Node.js Backend
    ↓
Gemini
```

## Text-to-Speech

```text
Gemini
    ↓
Nyra Response
    ↓
Azure Text-to-Speech
    ↓
Audio
    ↓
Live2D Lip Sync
```

**Current Voice:**

```text
pt-BR-YaraNeural
```

---

# Live2D and Lip Sync

O avatar é integrado ao sistema de áudio para criar sincronização labial em tempo real.

```text
Nyra Response
       ↓
      TTS
       ↓
     Audio
       ↓
   Lip Sync
       ↓
   Live2D Avatar
```

Também existem animações Idle aleatórias para evitar que o avatar permaneça completamente estático durante períodos de inatividade.

---

# Authentication and User Interface

A aplicação Unity possui:

* Tela de login
* IDs permitidos
* Avatar oculto antes da autenticação
* Chat com scroll automático
* Campo de entrada persistente
* Envio por Enter
* Botão de envio
* Mensagens diferenciadas entre usuário, Nyra e sistema
* Retry automático quando o backend está indisponível

---

# Infrastructure

Durante a execução, a aplicação pode iniciar automaticamente os serviços locais necessários.

```text
Unity
 │
 ├── Node.js :3000
 │
 └── Python Flask :5000
```

Ao fechar a aplicação, os processos iniciados pelo Unity também podem ser encerrados.

---

# Technology Stack

| Layer            | Technologies           |
| ---------------- | ---------------------- |
| Frontend         | Unity, C#, TextMeshPro |
| Avatar           | Live2D Cubism          |
| AI               | Google Gemini          |
| Backend          | Node.js, Express       |
| Python           | Flask, PyAutoGUI, PIL  |
| Speech           | Azure Speech Services  |
| Emotion Analysis | BERTweet-PT            |
| Computer Vision  | Gemini Vision          |
| Memory           | JSON                   |
| Communication    | HTTP / localhost       |

---

# Project Structure

Estrutura aproximada:

```text
Nyra/
│
├── Unity/
│   └── Minha IA vtuberoriginal/
│       ├── Assets/
│       ├── ProjectSettings/
│       └── ...
│
├── Backend/
│   ├── server.js
│   ├── memory.example.json
│   ├── package.json
│   └── ...
│
├── Emotion_AI/
│   ├── vision_agent.py
│   ├── emotion_server.py
│   └── ...
│
├── .env.example
├── .gitignore
├── LICENSE
└── README.md
```

---

# Installation

> **Nota:** as instruções abaixo ainda podem mudar conforme o projeto evolui.

## Prerequisites

* Unity
* Node.js
* Python
* Google Gemini API
* Azure Speech Services
* Live2D Cubism SDK

## 1. Clone the Repository

```bash
git clone SEU_REPOSITORIO
cd Nyra
```

## 2. Install Node.js Dependencies

```bash
npm install
```

## 3. Configure Environment Variables

Crie um arquivo:

```text
.env
```

Exemplo:

```env
GEMINI_API_KEY=
AZURE_SPEECH_KEY=
AZURE_SPEECH_REGION=
```

> **Nunca coloque suas chaves reais no GitHub.**

## 4. Configure Python

Instale as dependências necessárias:

```bash
pip install -r requirements.txt
```

## 5. Open the Unity Project

Abra o projeto pelo Unity Hub e execute:

```text
AIVTUBer2.unity
```

---

# Security

Este projeto utiliza serviços externos que exigem credenciais.

**Nunca publique:**

```text
.env
memory.json
API Keys
Tokens
Credenciais
Informações pessoais
```

Utilize:

```text
.env.example
memory.example.json
```

como modelos para configuração.

Se uma API key já tiver sido exposta publicamente, ela deve ser **revogada e substituída**.

---

# Project Status

> **Em desenvolvimento**

A Nyra é um projeto experimental em evolução.

Algumas partes do projeto ainda estão sendo refinadas e existem componentes legados provenientes de versões anteriores.

### Current System

```text
[OK] Unity + Live2D
[OK] Node.js Backend
[OK] Gemini
[OK] Azure STT
[OK] Azure TTS
[OK] Lip Sync
[OK] Persistent Memory
[OK] Vision Agent
[OK] Emotion Server

[WIP] Architecture Refinement
[WIP] Computer Vision Improvements
[WIP] Voice System Stabilization
```

---

# Roadmap

Possíveis próximos passos:

* [ ] Melhorar estabilidade do reconhecimento de voz
* [ ] Melhorar análise visual
* [ ] Expandir memória de longo prazo
* [ ] Melhorar sistema emocional
* [ ] Melhorar sincronização labial
* [ ] Sistema de ferramentas para programação
* [ ] Melhor gerenciamento de contexto
* [ ] Interface mais completa
* [ ] Sistema de plugins
* [ ] Maior autonomia da assistente

---

# Demonstrations

Vídeos e demonstrações do projeto serão publicados conforme o desenvolvimento avança.

> Em breve: demonstrações da Nyra conversando, analisando código, observando a tela e interagindo com o avatar Live2D.

---

# Legacy Components

O projeto possui componentes de versões anteriores.

Entre eles:

```text
Google Cloud Speech
Dialogflow
modelo.js
GeminiAI.cs
```

Esses componentes são mantidos principalmente para referência e histórico de desenvolvimento e **não representam necessariamente a arquitetura atual**.

---

# Project Goal

O objetivo da Nyra é explorar a construção de uma **assistente digital multimodal**, combinando:

```text
       Ouvir
          │
          ▼
Pensar ─────── Ver
          │
          ▼
       Conversar
          │
          ▼
       Expressar
```

A ideia é aproximar uma assistente de IA de uma experiência mais natural e interativa, combinando **conversação, memória, visão, voz e presença visual**.

---

# About the Project

**Nyra** é um projeto experimental independente focado em IA, VTubers, interação humano-computador e desenvolvimento de software.

> **Uma IA que não apenas responde — ela vê, ouve, lembra e interage.**

---

# License

Este projeto utiliza diferentes tecnologias e SDKs de terceiros. Consulte as respectivas licenças antes de redistribuir componentes externos.

Adicione aqui a licença escolhida para o código original da Nyra.
