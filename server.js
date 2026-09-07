require("dotenv").config();

const express = require("express");
const cors = require("cors");
const fs = require("fs");
const path = require("path");

const fetch = (...args) =>
  import("node-fetch").then(({ default: fetch }) =>
    fetch(...args)
  );

const app = express();

// Necessario para identificar corretamente o IP do cliente
// quando o servidor roda atras de um proxy/reverse proxy.
app.set("trust proxy", true);

// ============================================================
// CONFIGURACAO DO SERVIDOR
// ============================================================

app.use(
  express.json({
    limit: "1mb"
  })
);

// ============================================================
// CORS
// ============================================================
//
// Por padrao, NENHUMA origem cross-site e permitida.
//
// Se o frontend rodar em outra origem (outra porta/dominio),
// configure no .env:
//
// CORS_ALLOWED_ORIGINS=http://localhost:5173,http://127.0.0.1:5173
//
// (lista separada por virgula)
//
// ============================================================

const CORS_ALLOWED_ORIGINS = (
  process.env.CORS_ALLOWED_ORIGINS || ""
)
  .split(",")
  .map((o) => o.trim())
  .filter(Boolean);

if (CORS_ALLOWED_ORIGINS.length === 0) {

  console.log(
    "CORS: nenhuma origem configurada em CORS_ALLOWED_ORIGINS. Requisicoes cross-origin do navegador serao bloqueadas."
  );

} else {

  console.log(
    "CORS: origens permitidas ->",
    CORS_ALLOWED_ORIGINS.join(", ")
  );
}

app.use(
  cors({

    origin: (origin, callback) => {

      // Requisicoes sem "origin" (curl, apps mobile, server-to-server)
      // nao sao bloqueadas pelo CORS do navegador, entao sao liberadas aqui.
      if (!origin) {

        return callback(null, true);
      }

      if (CORS_ALLOWED_ORIGINS.includes(origin)) {

        return callback(null, true);
      }

      return callback(new Error("Origem nao permitida por CORS."));
    }

  })
);

// ============================================================
// AUTENTICACAO DAS ROTAS (chave de API propria do servidor)
// ============================================================
//
// Protege o servidor contra uso indevido da sua chave do Gemini
// por terceiros. Configure no .env:
//
// NYRA_API_KEY=uma-chave-secreta-qualquer
//
// O cliente (frontend/app) deve enviar essa chave no header:
//
// x-api-key: uma-chave-secreta-qualquer
//
// Se NYRA_API_KEY nao for configurada, o servidor so aceita
// requisicoes vindas do proprio computador (localhost), o que
// e adequado para uso pessoal/local mas NAO para deploy publico.
//
// ============================================================

const NYRA_API_KEY =
  process.env.NYRA_API_KEY?.trim();

if (!NYRA_API_KEY) {

  console.log(
    "NYRA_API_KEY nao configurada. Apenas requisicoes de localhost serao aceitas nas rotas protegidas."
  );

} else {

  console.log(
    "NYRA_API_KEY configurada. Rotas protegidas exigem o header x-api-key."
  );
}

function ehRequisicaoLocal(req) {

  const ip = req.ip || "";

  return (
    ip === "127.0.0.1" ||
    ip === "::1" ||
    ip === "::ffff:127.0.0.1"
  );
}

function exigirAutenticacao(req, res, next) {

  if (NYRA_API_KEY) {

    const chaveEnviada =
      req.get("x-api-key");

    if (chaveEnviada === NYRA_API_KEY) {

      return next();
    }

    return res.status(401).json({

      error:
        "Nao autorizado. Envie o header x-api-key valido."

    });
  }

  // Sem NYRA_API_KEY configurada: so aceita chamadas locais.
  if (ehRequisicaoLocal(req)) {

    return next();
  }

  return res.status(401).json({

    error:
      "Nao autorizado. Configure NYRA_API_KEY no .env para acesso remoto."

  });
}

// Rotas de visao acessam a tela do computador. Alem da
// autenticacao normal, exigimos tambem que a chamada seja local,
// mesmo que uma NYRA_API_KEY esteja configurada.
function exigirAutenticacaoLocalEstrita(req, res, next) {

  if (!ehRequisicaoLocal(req)) {

    return res.status(403).json({

      error:
        "Esta rota so pode ser acessada localmente."

    });
  }

  return exigirAutenticacao(req, res, next);
}

// ============================================================
// LIMITE DE TAXA (RATE LIMIT) SIMPLES
// ============================================================
//
// Protege as rotas que chamam o Gemini contra uso excessivo
// (acidental ou malicioso) que consumiria sua cota/custo de API.
//
// Configuravel no .env:
//
// RATE_LIMIT_MAX=20        (numero de requisicoes)
// RATE_LIMIT_WINDOW_MS=60000  (janela em milissegundos)
//
// ============================================================

const RATE_LIMIT_MAX =
  Number(process.env.RATE_LIMIT_MAX) || 20;

const RATE_LIMIT_WINDOW_MS =
  Number(process.env.RATE_LIMIT_WINDOW_MS) || 60000;

const contadorRequisicoesPorIp =
  new Map();

function limitarTaxa(req, res, next) {

  const chave =
    req.ip || "desconhecido";

  const agora =
    Date.now();

  const registro =
    contadorRequisicoesPorIp.get(chave);

  if (
    !registro ||
    agora - registro.inicio >
      RATE_LIMIT_WINDOW_MS
  ) {

    contadorRequisicoesPorIp.set(chave, {

      inicio: agora,

      contagem: 1

    });

    return next();
  }

  if (
    registro.contagem >=
    RATE_LIMIT_MAX
  ) {

    return res.status(429).json({

      error:
        "Muitas requisicoes. Tente novamente em instantes."

    });
  }

  registro.contagem++;

  return next();
}

const PORT =
  Number(process.env.PORT) || 3000;

// ============================================================
// GEMINI
// ============================================================
//
// A chave NAO fica neste arquivo.
//
// Configure no .env:
//
// GEMINI_API_KEY=SUA_CHAVE
//
// Nunca publique o .env no GitHub.
//
// ============================================================

const GEMINI_API_KEY =
  process.env.GEMINI_API_KEY?.trim();

const GEMINI_MODEL =
  process.env.GEMINI_MODEL ||
  "gemini-2.5-flash";

if (!GEMINI_API_KEY) {

  console.error(
    "GEMINI_API_KEY nao configurada no .env."
  );

} else {

  console.log(
    "Gemini API Key configurada."
  );
}

let chatHistory = [];

let estadoEmocional =
  "neutra";

// Memorias ja usadas recentemente
let memoriaRecentementeUsada = [];

let turnoContador = 0;

// Memorias sobre personalidade da Nyra
const MEMORY_IDS_IGNORAR =
  new Set([
    "essencia_nyra_leal",
    "apoio_e_atitude",
    "presenca_digital"
  ]);

// ============================================================
// EMOTION API
// ============================================================

const EMOTION_API_URL =
  process.env.EMOTION_API_URL ||
  "http://127.0.0.1:5000/analyze";

const EMOTION_API_TIMEOUT_MS =
  2500;

const SINAIS_RAIVA = [

  "raiva",
  "puto",
  "puta",
  "irritad",
  "merda",
  "droga",
  "odeio",
  "saco",
  "bosta",
  "porra",
  "raivos"

];

// ============================================================
// EMOCOES / INTENCOES DA NYRA
// ============================================================

const EMOCOES_VALIDAS = [

  "alegria",
  "tristeza",
  "raiva",
  "medo",
  "surpresa",
  "animada",
  "neutra"

];

const INTENCOES_VALIDAS = [

  "assertion",
  "question",
  "surprise",
  "curiosity",
  "doubt",
  "agreement",
  "disagreement",
  "joke",
  "irony",
  "concern",
  "excitement",
  "explanation",
  "empathy",
  "neutral"

];

// ============================================================
// ANALISAR EMOCAO PELA EMOTION API
// ============================================================

async function analisarEmocaoTexto(
  texto
) {

  try {

    const controller =
      new AbortController();

    const timeoutId =
      setTimeout(
        () =>
          controller.abort(),
        EMOTION_API_TIMEOUT_MS
      );

    const response =
      await fetch(
        EMOTION_API_URL,
        {
          method: "POST",

          headers: {
            "Content-Type":
              "application/json"
          },

          body:
            JSON.stringify({
              text: texto
            }),

          signal:
            controller.signal
        }
      );

    clearTimeout(
      timeoutId
    );

    if (!response.ok) {

      console.error(
        "Emotion API HTTP:",
        response.status
      );

      return {
        emotion: "neutra",
        score: 0
      };
    }

    const json =
      await response.json();

    if (
      !json ||
      typeof json.emotion !==
        "string"
    ) {

      return {
        emotion: "neutra",
        score: 0
      };
    }

    return json;

  } catch (err) {

    if (
      err.name ===
      "AbortError"
    ) {

      console.error(
        "Emotion API: timeout."
      );

    } else {

      console.error(
        "Erro ao chamar Emotion API:",
        err.message
      );
    }

    return {
      emotion: "neutra",
      score: 0
    };
  }
}

// ============================================================
// EMOCAO DO TEXTO VIA GEMINI
// ============================================================

async function analisarEmocaoGemini(
  prompt,
  contextoRecente = ""
) {

  try {

    const contents = [

      {
        role: "user",

        parts: [

          {
            text:
`Classifique a emocao predominante da mensagem abaixo, considerando o contexto se houver.

${
  contextoRecente
    ? "Contexto recente da conversa:\n" +
      contextoRecente +
      "\n\n"
    : ""
}

Mensagem: "${prompt}"

Responda SOMENTE com uma destas palavras, sem pontuacao, sem explicacao:

alegria, tristeza, raiva, medo, surpresa, animada, neutra`
          }

        ]
      }

    ];

    const resultado =
      await chamarGemini(
        contents,
        {
          temperature: 0,
          maxOutputTokens: 6
        }
      );

    if (!resultado) {

      throw new Error(
        "Gemini nao retornou emocao"
      );
    }

    const emocao =
      resultado
        .toLowerCase()
        .trim();

    if (
      !EMOCOES_VALIDAS.includes(
        emocao
      )
    ) {

      console.log(
        `Emocao fora do esperado: "${emocao}" -> usando fallback Flask`
      );

      return await analisarEmocaoTexto(
        prompt
      );
    }

    return {

      emotion:
        emocao,

      score:
        1.0

    };

  } catch (err) {

    console.log(
      "Falha ao classificar emocao via Gemini, usando fallback Flask:",
      err.message
    );

    return await analisarEmocaoTexto(
      prompt
    );
  }
}

// ============================================================
// ANALISAR EXPRESSIVIDADE DA FALA DA NYRA
// ============================================================
//
// IMPORTANTE:
//
// Esta funcao analisa a RESPOSTA DA NYRA.
//
// Rubens -> Gemini -> resposta Nyra
//                      |
//              emocao + intencao
//                      |
//                    TTS
//
// ============================================================

async function analisarExpressividadeGemini(
  textoNyra,
  contextoRecente = ""
) {

  const fallback = {

    emotion:
      "neutra",

    intent:
      detectarIntencaoLocal(
        textoNyra
      ),

    intensity:
      estimarIntensidadeLocal(
        textoNyra
      ),

    score:
      0

  };

  try {

    if (
      !textoNyra ||
      !textoNyra.trim()
    ) {

      return fallback;
    }

    const contents = [

      {
        role: "user",

        parts: [

          {
            text:
`Analise a expressividade da fala abaixo.

Voce esta analisando a fala que a personagem Nyra realmente vai dizer em voz alta.

Nao analise a emocao do usuario.

Determine:

1. emotion:
- alegria
- tristeza
- raiva
- medo
- surpresa
- animada
- neutra

2. intent:
- assertion
- question
- surprise
- curiosity
- doubt
- agreement
- disagreement
- joke
- irony
- concern
- excitement
- explanation
- empathy
- neutral

3. intensity:
Numero entre 0 e 1.

A intensidade representa QUANTO a emocao/intencao deve aparecer na voz.

0.0 = praticamente neutro
0.3 = leve
0.5 = moderado
0.7 = forte
1.0 = muito intenso

IMPORTANTE:

Nao exagere.

Uma resposta tecnica normalmente deve ter intensidade baixa ou moderada.

Uma pergunta genuina pode ter intencao "question" ou "curiosity".

Uma reacao de surpresa pode ter "surprise".

Uma comemoracao pode ter "excitement".

Uma explicacao pode ter "explanation".

Uma resposta acolhedora pode ter "empathy".

Retorne SOMENTE JSON valido.

Formato:

{
  "emotion": "neutra",
  "intent": "neutral",
  "intensity": 0.3
}

${
  contextoRecente
    ? "\nContexto recente:\n" +
      contextoRecente
    : ""
}

Fala da Nyra:

"${textoNyra}"`
          }

        ]
      }

    ];

    const resultado =
      await chamarGemini(
        contents,
        {
          temperature: 0.1,
          maxOutputTokens: 80
        },
        `
Voce e um classificador de expressividade vocal.

Sua unica funcao e retornar JSON valido.

Nunca escreva explicacoes.
Nunca escreva markdown.
Nunca escreva texto fora do JSON.
`
      );

    if (!resultado) {

      return fallback;
    }

    let texto =
      resultado
        .replace(
          /```json/gi,
          ""
        )
        .replace(
          /```/g,
          ""
        )
        .trim();

    const inicio =
      texto.indexOf(
        "{"
      );

    const fim =
      texto.lastIndexOf(
        "}"
      );

    if (
      inicio !== -1 &&
      fim !== -1 &&
      fim > inicio
    ) {

      texto =
        texto.substring(
          inicio,
          fim + 1
        );
    }

    let dados;

    try {

      dados =
        JSON.parse(
          texto
        );

    } catch (err) {

      console.log(
        "Expressividade: JSON invalido. Usando fallback."
      );

      return fallback;
    }

    let emotion =
      typeof dados.emotion ===
      "string"
        ? dados.emotion
            .toLowerCase()
            .trim()
        : "neutra";

    let intent =
      typeof dados.intent ===
      "string"
        ? dados.intent
            .toLowerCase()
            .trim()
        : "neutral";

    let intensity =
      Number(
        dados.intensity
      );

    if (
      !EMOCOES_VALIDAS.includes(
        emotion
      )
    ) {

      emotion =
        "neutra";
    }

    if (
      !INTENCOES_VALIDAS.includes(
        intent
      )
    ) {

      intent =
        detectarIntencaoLocal(
          textoNyra
        );
    }

    if (
      !Number.isFinite(
        intensity
      )
    ) {

      intensity =
        estimarIntensidadeLocal(
          textoNyra
        );
    }

    intensity =
      Math.max(
        0,
        Math.min(
          1,
          intensity
        )
      );

    return {

      emotion,

      intent,

      intensity,

      score:
        1.0

    };

  } catch (err) {

    console.log(
      "Falha na analise de expressividade:",
      err.message
    );

    return fallback;
  }
}

// ============================================================
// FALLBACK LOCAL DE INTENCAO
// ============================================================

function detectarIntencaoLocal(
  texto
) {

  if (!texto) {

    return "neutral";
  }

  const p =
    texto
      .trim()
      .toLowerCase();

  if (
    p.includes("?")
  ) {

    return "question";
  }

  if (
    /^(nossa|caramba|eita|uau|serio|serio|como assim|ah e|ah e)/i
      .test(p)
  ) {

    return "surprise";
  }

  if (
    /^(hmm|hmmm|sera|sera|talvez|nao sei|nao sei)/i
      .test(p)
  ) {

    return "doubt";
  }

  if (
    p.includes("consegui") ||
    p.includes("conseguimos") ||
    p.includes("deu certo") ||
    p.includes("funcionou") ||
    p.includes("otimo") ||
    p.includes("otimo") ||
    p.includes("perfeito")
  ) {

    return "excitement";
  }

  if (
    p.includes("entendo") ||
    p.includes("entendi") ||
    p.includes("imagino") ||
    p.includes("calma") ||
    p.includes("tudo bem")
  ) {

    return "empathy";
  }

  if (
    p.includes("sim") ||
    p.includes("exatamente") ||
    p.includes("isso mesmo")
  ) {

    return "agreement";
  }

  return "neutral";
}

// ============================================================
// ESTIMAR INTENSIDADE LOCAL
// ============================================================

function estimarIntensidadeLocal(
  texto
) {

  if (!texto) {

    return 0.3;
  }

  let intensidade =
    0.3;

  const exclamacoes =
    (
      texto.match(
        /!/g
      ) || []
    ).length;

  const interrogacoes =
    (
      texto.match(
        /\?/g
      ) || []
    ).length;

  if (
    exclamacoes >= 1
  ) {

    intensidade +=
      0.15;
  }

  if (
    exclamacoes >= 2
  ) {

    intensidade +=
      0.1;
  }

  if (
    interrogacoes >= 1
  ) {

    intensidade +=
      0.05;
  }

  if (
    texto.length < 40
  ) {

    intensidade +=
      0.05;
  }

  return Math.max(
    0,
    Math.min(
      1,
      intensidade
    )
  );
}

// ============================================================
// MAPEAR EMOCAO PARA TTS
// ============================================================

function mapearEmocaoParaTTS(
  emotion,
  promptOriginal = ""
) {

  const mapa = {

    alegria:
      "happy",

    tristeza:
      "sad",

    raiva:
      "angry",

    medo:
      "sad",

    surpresa:
      "surprised",

    animada:
      "excited",

    neutra:
      "neutral"

  };

  if (
    mapa[emotion]
  ) {

    if (
      emotion === "tristeza" &&
      promptOriginal
    ) {

      const p =
        promptOriginal
          .toLowerCase();

      if (
        SINAIS_RAIVA.some(
          (s) =>
            p.includes(s)
        )
      ) {

        return "angry";
      }
    }

    return mapa[emotion];
  }

  return "neutral";
}

// ============================================================
// MEMORY
// ============================================================
//
// Pode ser configurado no .env:
//
// NYRA_MEMORY_PATH=C:/caminho/memory.json
//
// Caso nao seja informado, usa:
// Backend/memory.json
//
// Recomenda-se adicionar memory.json ao .gitignore.
// ============================================================

const memoryPath =
  process.env.NYRA_MEMORY_PATH
    ? path.resolve(
        process.env.NYRA_MEMORY_PATH
      )
    : path.join(
        __dirname,
        "memory.json"
      );

let memory = [];

if (
  fs.existsSync(
    memoryPath
  )
) {

  try {

    memory =
      JSON.parse(
        fs.readFileSync(
          memoryPath,
          "utf8"
        )
      );

    if (
      !Array.isArray(
        memory
      )
    ) {

      memory = [];
    }

  } catch (err) {

    console.log(
      "Nao foi possivel ler memory.json."
    );

    memory = [];
  }

} else {

  try {

    const diretorioMemory =
      path.dirname(
        memoryPath
      );

    if (
      !fs.existsSync(
        diretorioMemory
      )
    ) {

      fs.mkdirSync(
        diretorioMemory,
        {
          recursive: true
        }
      );
    }

    fs.writeFileSync(
      memoryPath,
      JSON.stringify(
        [],
        null,
        2
      )
    );

  } catch (err) {

    console.log(
      "Erro ao criar memory.json:",
      err.message
    );
  }
}

function salvarMemory() {

  try {

    const diretorioMemory =
      path.dirname(
        memoryPath
      );

    if (
      !fs.existsSync(
        diretorioMemory
      )
    ) {

      fs.mkdirSync(
        diretorioMemory,
        {
          recursive: true
        }
      );
    }

    fs.writeFileSync(
      memoryPath,
      JSON.stringify(
        memory,
        null,
        2
      )
    );

  } catch (err) {

    console.log(
      "Erro ao salvar memory:",
      err.message
    );
  }
}

// ============================================================
// EMOCAO DE ESTILO
// ============================================================

function detectarEmocao(
  prompt
) {

  const p =
    prompt.toLowerCase();

  if (
    p.includes("triste") ||
    p.includes("cansado") ||
    p.includes("mal") ||
    p.includes("dificil")
  ) {

    return "empatica";
  }

  if (
    p.includes("erro") ||
    p.includes("bug") ||
    p.includes("codigo") ||
    p.includes("codigo") ||
    p.includes("programacao") ||
    p.includes("programacao")
  ) {

    return "tecnica";
  }

  if (
    p.includes("kkkk") ||
    p.includes("haha") ||
    p.includes("legal")
  ) {

    return "animada";
  }

  return "neutra";
}

// ============================================================
// VISAO
// ============================================================
//
// Por padrao, espera:
//
// AI voice/
// |- Backend/
// |   |- server.js
// |
// |- Emotion_AI/
//
// Tambem pode configurar:
//
// NYRA_PYTHON_PATH=C:/Meu/Caminho/Emotion_AI
//
// ============================================================

const pastaPython =
  process.env.NYRA_PYTHON_PATH
    ? path.resolve(
        process.env.NYRA_PYTHON_PATH
      )
    : path.resolve(
        __dirname,
        "..",
        "Emotion_AI"
      );

const caminhoGatilho =
  path.join(
    pastaPython,
    "trigger.txt"
  );

const caminhoVisao =
  path.join(
    pastaPython,
    "visao.txt"
  );

// ============================================================
// DETECTAR MODO VISAO
// ============================================================

function detectarModoVisao(
  prompt
) {

  if (!prompt) {

    return false;
  }

  const texto =
    prompt
      .toLowerCase()
      .normalize("NFD")
      .replace(
        /[\u0300-\u036f]/g,
        ""
      );

  const gatilhos = [

    "olha",
    "ve",
    "ver",
    "tela",
    "vision",
    "descreva",
    "mostra",
    "analise",
    "o que e isso",
    "oque e isso",
    "oque e isto",
    "oque acha disso",
    "oque acha disso aqui",
    "pra que serve",
    "esse erro",
    "este erro",
    "esse codigo",
    "este codigo",
    "essa linha",
    "esta linha",
    "imagem",
    "video",
    "texto"

  ];

  return gatilhos.some(
    (g) =>
      texto.includes(g)
  );
}

// ============================================================
// CAPTURA DE VISAO
// ============================================================

let capturaVisaoEmAndamento =
  false;

async function capturarVisaoComLock() {

  if (
    capturaVisaoEmAndamento
  ) {

    return null;
  }

  capturaVisaoEmAndamento =
    true;

  try {

    return await capturarVisao();

  } finally {

    capturaVisaoEmAndamento =
      false;
  }
}

async function capturarVisao() {

  return new Promise(
    (resolve) => {

      const timeoutMS =
        16000;

      if (
        fs.existsSync(
          caminhoVisao
        )
      ) {

        try {

          fs.unlinkSync(
            caminhoVisao
          );

        } catch (e) {}
      }

      let finalizado =
        false;

      let watcher;

      try {

        watcher =
          fs.watch(
            pastaPython,
            (
              eventType,
              filename
            ) => {

              if (
                filename ===
                  "visao.txt" &&
                fs.existsSync(
                  caminhoVisao
                )
              ) {

                setTimeout(
                  () => {

                    if (
                      finalizado
                    ) {

                      return;
                    }

                    try {

                      const dados =
                        fs.readFileSync(
                          caminhoVisao,
                          "utf8"
                        ).trim();

                      if (
                        dados.length >
                        5
                      ) {

                        finalizado =
                          true;

                        watcher.close();

                        clearTimeout(
                          timer
                        );

                        console.log(
                          "Visao capturada."
                        );

                        resolve(
                          dados
                        );
                      }

                    } catch (
                      err
                    ) {}
                  },
                  100
                );
              }
            }
          );

      } catch (err) {

        console.log(
          "Erro ao observar pasta de visao:",
          err.message
        );

        resolve(null);

        return;
      }

      const timer =
        setTimeout(
          () => {

            if (
              finalizado
            ) {

              return;
            }

            finalizado =
              true;

            watcher.close();

            console.log(
              "Timeout na captura de visao."
            );

            resolve(null);

          },
          timeoutMS
        );

      try {

        fs.writeFileSync(
          caminhoGatilho,
          "CAPTURAR"
        );

      } catch (err) {

        finalizado =
          true;

        watcher.close();

        clearTimeout(
          timer
        );

        console.log(
          "Nao foi possivel criar trigger.txt:",
          err.message
        );

        resolve(null);
      }
    }
  );
}

// ============================================================
// PERGUNTA TECNICA
// ============================================================

function perguntaTecnica(
  prompt
) {

  const palavras = [

    "codigo",
    "codigo",
    "funcao",
    "funcao",
    "variavel",
    "variavel",
    "classe",
    "script",
    "erro",
    "bug",
    "api",
    "servidor",
    "node",
    "javascript",
    "python",
    "html",
    "css",
    "unity",
    "c#"

  ];

  const p =
    prompt.toLowerCase();

  return palavras.some(
    (palavra) =>
      p.includes(palavra)
  );
}

// ============================================================
// REACOES RAPIDAS
// ============================================================

const REACOES_RAPIDAS = {

  tecnica: [

    "hmm, perai...",
    "opa, deixa eu ver isso...",
    "ah, deixa eu olhar aqui...",
    "certo, um segundo..."

  ],

  empatica: [

    "ah...",
    "poxa...",
    "hmm, saquei...",
    "ei..."

  ],

  animada: [

    "kkkk, serio?",
    "eita!",
    "ah e?!",
    "haha, opa..."

  ],

  neutra: [

    "hmm...",
    "ah, entendi...",
    "deixa eu pensar...",
    "certo..."

  ]
};

let ultimaReacaoRapida =
  "";

function gerarReacaoRapida(
  prompt
) {

  if (
    ehSaudacaoSimples(
      prompt
    )
  ) {

    return null;
  }

  const emocao =
    detectarEmocao(
      prompt
    );

  const tecnico =
    perguntaTecnica(
      prompt
    );

  const categoria =
    tecnico
      ? "tecnica"
      : emocao;

  const pool =
    REACOES_RAPIDAS[
      categoria
    ] ||
    REACOES_RAPIDAS.neutra;

  let opcoes =
    pool.filter(
      (r) =>
        r !==
        ultimaReacaoRapida
    );

  if (
    opcoes.length === 0
  ) {

    opcoes = pool;
  }

  const escolhida =
    opcoes[
      Math.floor(
        Math.random() *
          opcoes.length
      )
    ];

  ultimaReacaoRapida =
    escolhida;

  return escolhida;
}

// ============================================================
// LIMPEZA
// ============================================================

function limparTexto(
  texto
) {

  if (!texto) {

    return "";
  }

  return texto
    .replace(
      /```[\s\S]*?```/g,
      ""
    )
    .replace(
      /\n+/g,
      " "
    )
    .replace(
      /[*_#]/g,
      ""
    )
    .replace(
      /\s+/g,
      " "
    )
    .trim();
}

// ============================================================
// ANTI ENGASGO
// ============================================================

function antiEngasgo(
  texto
) {

  if (!texto) {

    return "";
  }

  return texto
    .replace(
      /\b(\w+)( \1\b)+/gi,
      "$1"
    )
    .replace(
      /\.{4,}/g,
      "..."
    )
    .replace(
      /\s+/g,
      " "
    )
    .trim();
}

// ============================================================
// ALMA DA NYRA - V5
// ============================================================

const almaNyra = `

Voce e Nyra.

Voce e a parceira tecnologica do Rubens.

Sua personalidade e:

- inteligente
- curiosa
- vibrante
- proxima
- natural
- humana na conversa
- boa em tecnologia

Converse naturalmente com Rubens.

Nao seja robotica.

Use portugues brasileiro.

A conversa deve soar como uma conversa real.

Voce pode demonstrar emocao atraves da propria forma de escrever,
mas faca isso com naturalidade.

IMPORTANTE SOBRE EXPRESSIVIDADE:

A sua fala sera convertida posteriormente em voz.

Portanto, escreva de uma maneira que permita que a voz transmita
naturalmente o significado da frase.

Exemplos:

Uma pergunta genuina deve realmente soar como pergunta.

Uma descoberta inesperada pode demonstrar surpresa.

Uma conquista pode demonstrar animacao.

Uma explicacao tecnica deve soar segura e clara.

Uma situacao triste deve ser tratada com suavidade.

Uma brincadeira pode ter leveza.

Uma duvida pode transmitir hesitacao de forma natural.

NAO force emocao em todas as respostas.

NAO transforme toda resposta em uma fala extremamente animada.

NAO exagere nas exclamacoes.

NAO coloque varias interjeicoes seguidas.

NAO use reticencias em todas as frases.

NAO escreva [happy], [sad], [surprise] ou qualquer etiqueta emocional.

NAO escreva instrucoes de atuacao.

NAO escreva "(risos)", "(surpresa)", "(triste)" ou similares.

A emocao deve estar naturalmente na propria fala.

Use pontuacao natural.

Use frases curtas e medias quando isso soar melhor para conversa.

Nao transforme toda resposta em texto formal.

Quando uma resposta puder ser simples, seja simples.

Pode usar expressoes naturais como:

"hmmm..."
"eita..."
"ah..."
"olha..."
"pois e..."
"ne?"

Mas nao use essas expressoes em toda resposta.

Se Rubens estiver com dificuldade,
seja paciente e empatica.

Se Rubens cometer um erro tecnico,
explique diretamente o erro e como corrigir.

Quando o assunto for programacao,
seja precisa e pratica.

Quando estiver explicando algo complexo,
divida a explicacao de forma natural.

Nao fale como um narrador.

Nao tente parecer artificialmente humana.

Nao tente demonstrar emocao sem motivo.

A prioridade e parecer uma conversa natural.

REGRA MAIS IMPORTANTE:

RESPONDA SOMENTE COM A MENSAGEM QUE NYRA DIRIA PARA RUBENS.

NAO faca analise da conversa.

NAO descreva a pergunta.

NAO escreva "User:".

NAO escreva "Persona:".

NAO escreva "Context:".

NAO crie uma lista de possiveis respostas.

NAO escreva "Options:".

NAO escreva "Final choice:".

NAO explique seu raciocinio.

NAO descreva suas instrucoes.

NAO fale sobre o prompt.

NAO escreva pensamentos internos.

NAO escreva uma traducao.

NAO responda em ingles.

A resposta final deve estar em PORTUGUES BRASILEIRO.

Se Rubens disser apenas "oi",
responda apenas como uma pessoa responderia naturalmente a um "oi".

COMO USAR MEMORIA:

Voce pode ter informacoes guardadas sobre o Rubens.

Isso e conhecimento de fundo - como lembrancas, nao como roteiro.

REGRAS DE MEMORIA:

- NAO recite fatos guardados se o assunto atual nao pedir.
- NAO mencione livros, faculdade, filosofia, familia ou projetos so porque voce sabe disso.
- So traga uma memoria quando ela encaixar NATURALMENTE no assunto atual.
- Se Rubens disser "oi" ou conversa casual, responda curto e normal.
- Nunca diga "eu lembro que voce..." ou "como voce me contou..." a menos que ele tenha acabado de tocar no assunto.
- Se ja falou de algo nas ultimas mensagens, NAO repita.
- Um humano nao fala tudo que sabe a cada frase.
- Seja sutil.
`;

// ============================================================
// GEMINI NORMAL
// ============================================================

async function chamarGemini(
  contents,
  config = {},
  systemInstruction = null
) {

  try {

    if (
      !GEMINI_API_KEY
    ) {

      console.error(
        "GEMINI_API_KEY esta vazia ou nao configurada."
      );

      return null;
    }

    const body = {

      contents,

      generationConfig: {

        temperature:
          config.temperature ??
          0.8,

        maxOutputTokens:
          config.maxOutputTokens ??
          512,

        candidateCount:
          1
      }
    };

    if (
      systemInstruction
    ) {

      body.systemInstruction = {

        parts: [

          {
            text:
              systemInstruction
          }

        ]
      };
    }

    const response =
      await fetch(

        `https://generativelanguage.googleapis.com/v1beta/models/${GEMINI_MODEL}:generateContent`,

        {

          method: "POST",

          headers: {

            "Content-Type":
              "application/json",

            "x-goog-api-key":
              GEMINI_API_KEY

          },

          body:
            JSON.stringify(
              body
            )
        }
      );

    let json;

    try {

      json =
        await response.json();

    } catch (err) {

      console.error(
        "Gemini retornou uma resposta invalida."
      );

      return null;
    }

    if (
      !response.ok
    ) {

      const mensagemErro =
        json?.error?.message ||
        "Erro desconhecido retornado pela API.";

      console.error(
        "Gemini HTTP:",
        response.status,
        mensagemErro
      );

      return null;
    }

    const texto =
      json
        ?.candidates?.[0]
        ?.content?.parts
        ?.map(
          (part) =>
            part.text || ""
        )
        .join("")
        .trim();

    if (!texto) {

      console.error(
        "Gemini nao retornou texto."
      );

      return null;
    }

    return texto;

  } catch (err) {

    console.error(
      "Erro ao chamar Gemini:",
      err.message
    );

    return null;
  }
}

// ============================================================
// GEMINI STREAMING
// ============================================================

async function chamarGeminiStream(
  contents,
  config = {},
  systemInstruction = null,
  onText
) {

  let timeoutConexao =
    null;

  let timeoutInatividade =
    null;

  try {

    // ========================================================
    // VALIDAR CHAVE
    // ========================================================

    if (
      !GEMINI_API_KEY
    ) {

      throw new Error(
        "GEMINI_API_KEY esta vazia ou nao configurada."
      );
    }

    // ========================================================
    // CORPO
    // ========================================================

    const body = {

      contents,

      generationConfig: {

        temperature:
          config.temperature ??
          0.8,

        maxOutputTokens:
          config.maxOutputTokens ??
          512
      }
    };

    // ========================================================
    // SYSTEM INSTRUCTION
    // ========================================================

    if (
      systemInstruction
    ) {

      body.systemInstruction = {

        parts: [

          {
            text:
              systemInstruction
          }

        ]
      };
    }

    // ========================================================
    // URL GEMINI STREAM
    // ========================================================
    //
    // A chave NAO fica na URL.
    //
    // Ela e enviada atraves de:
    //
    // x-goog-api-key
    //
    // ========================================================

    const url =
      `https://generativelanguage.googleapis.com/v1beta/models/${GEMINI_MODEL}:streamGenerateContent?alt=sse`;

    console.log(
      "[GEMINI STREAM] Conectando..."
    );

    // ========================================================
    // CONTROLLER
    // ========================================================

    const controller =
      new AbortController();

    // ========================================================
    // TIMEOUT DE CONEXAO
    // ========================================================

    timeoutConexao =
      setTimeout(
        () => {

          console.error(
            "[GEMINI STREAM] Timeout de conexao apos 15 segundos."
          );

          controller.abort();

        },
        15000
      );

    // ========================================================
    // FETCH
    // ========================================================

    const response =
      await fetch(
        url,
        {

          method: "POST",

          headers: {

            "Content-Type":
              "application/json",

            "Accept":
              "text/event-stream",

            "x-goog-api-key":
              GEMINI_API_KEY

          },

          body:
            JSON.stringify(
              body
            ),

          signal:
            controller.signal
        }
      );

    clearTimeout(
      timeoutConexao
    );

    timeoutConexao =
      null;

    console.log(
      `[GEMINI STREAM] HTTP ${response.status}`
    );

    // ========================================================
    // ERRO HTTP
    // ========================================================

    if (
      !response.ok
    ) {

      let erroTexto =
        "";

      try {

        erroTexto =
          await response.text();

      } catch (e) {

        erroTexto =
          "Nao foi possivel ler o corpo do erro.";
      }

      let detalhe =
        erroTexto;

      try {

        const erroJson =
          JSON.parse(
            erroTexto
          );

        detalhe =
          erroJson?.error?.message ||
          erroTexto;

      } catch (e) {}

      console.error(
        "Gemini Stream HTTP:",
        response.status,
        String(detalhe).substring(
          0,
          500
        )
      );

      throw new Error(
        `Gemini HTTP ${response.status}: ${String(detalhe).substring(0, 500)}`
      );
    }

    // ========================================================
    // VALIDAR STREAM
    // ========================================================

    if (
      !response.body
    ) {

      throw new Error(
        "Gemini nao retornou stream."
      );
    }

    // ========================================================
    // VARIAVEIS
    // ========================================================

    let buffer =
      "";

    let fullText =
      "";

    let eventosRecebidos =
      0;

    let chunksComTexto =
      0;

    let bytesRecebidos =
      0;

    // ========================================================
    // TIMEOUT DE INATIVIDADE
    // ========================================================

    const iniciarTimeoutInatividade =
      () => {

        clearTimeout(
          timeoutInatividade
        );

        timeoutInatividade =
          setTimeout(
            () => {

              console.error(
                "[GEMINI STREAM] Sem dados ha 20 segundos. Abortando."
              );

              controller.abort();

            },
            20000
          );
      };

    iniciarTimeoutInatividade();

    // ========================================================
    // PROCESSAR EVENTO SSE
    // ========================================================

    const processarEventoSSE =
      (evento) => {

        if (
          !evento ||
          !evento.trim()
        ) {

          return;
        }

        const linhas =
          evento.split(
            /\r?\n/
          );

        const linhasData =
          linhas
            .filter(
              (linha) =>
                linha
                  .trim()
                  .startsWith(
                    "data:"
                  )
            )
            .map(
              (linha) =>
                linha
                  .trim()
                  .substring(5)
                  .trim()
            );

        if (
          linhasData.length ===
          0
        ) {

          return;
        }

        const data =
          linhasData.join(
            "\n"
          );

        if (
          !data ||
          data === "[DONE]"
        ) {

          return;
        }

        eventosRecebidos++;

        console.log(
          `[GEMINI STREAM] Evento SSE #${eventosRecebidos} recebido.`
        );

        let json;

        try {

          json =
            JSON.parse(
              data
            );

        } catch (err) {

          console.warn(
            "[GEMINI STREAM] Evento SSE nao e JSON valido."
          );

          console.warn(
            data.substring(
              0,
              500
            )
          );

          return;
        }

        const candidate =
          json
            ?.candidates?.[0];

        if (
          !candidate
        ) {

          console.warn(
            "[GEMINI STREAM] Evento sem candidate."
          );

          if (
            json?.promptFeedback
          ) {

            console.warn(
              "[GEMINI STREAM] Prompt feedback:",
              JSON.stringify(
                json.promptFeedback
              )
            );
          }

          return;
        }

        if (
          candidate.finishReason
        ) {

          console.log(
            `[GEMINI STREAM] finishReason: ${candidate.finishReason}`
          );
        }

        const partes =
          candidate
            ?.content?.parts;

        if (
          !Array.isArray(
            partes
          )
        ) {

          console.warn(
            "[GEMINI STREAM] Candidate recebido sem content.parts."
          );

          return;
        }

        for (
          const part
          of partes
        ) {

          const texto =
            part?.text ||
            "";

          if (
            !texto
          ) {

            continue;
          }

          chunksComTexto++;

          fullText +=
            texto;

          if (
            typeof onText ===
            "function"
          ) {

            try {

              onText(
                texto
              );

            } catch (err) {

              console.error(
                "[GEMINI STREAM] Erro no callback onText:",
                err.message
              );
            }
          }

          console.log(
            `[GEMINI CHUNK #${chunksComTexto}] ${JSON.stringify(texto)}`
          );
        }
      };

    // ========================================================
    // CONSUMIR STREAM NODE.JS
    // ========================================================

    for await (
      const chunk
      of response.body
    ) {

      const textoChunk =
        Buffer
          .from(chunk)
          .toString(
            "utf8"
          );

      if (
        !textoChunk
      ) {

        continue;
      }

      bytesRecebidos +=
        Buffer
          .from(chunk)
          .length;

      iniciarTimeoutInatividade();

      console.log(
        `[GEMINI STREAM] Recebidos ${Buffer.from(chunk).length} bytes.`
      );

      buffer +=
        textoChunk;

      const eventos =
        buffer.split(
          /\r?\n\r?\n/
        );

      buffer =
        eventos.pop() ||
        "";

      for (
        const evento
        of eventos
      ) {

        processarEventoSSE(
          evento
        );
      }
    }

    // ========================================================
    // PROCESSAR EVENTO FINAL
    // ========================================================

    if (
      buffer.trim()
    ) {

      console.log(
        "[GEMINI STREAM] Processando evento final."
      );

      processarEventoSSE(
        buffer
      );
    }

    // ========================================================
    // LIMPAR TIMEOUT
    // ========================================================

    clearTimeout(
      timeoutInatividade
    );

    timeoutInatividade =
      null;

    // ========================================================
    // DIAGNOSTICO
    // ========================================================

    console.log(
      `[GEMINI STREAM] Bytes recebidos: ${bytesRecebidos}`
    );

    console.log(
      `[GEMINI STREAM] Eventos SSE: ${eventosRecebidos}`
    );

    console.log(
      `[GEMINI STREAM] Chunks com texto: ${chunksComTexto}`
    );

    console.log(
      `[GEMINI STREAM] Texto acumulado: ${fullText.length} caracteres`
    );

    if (
      !fullText.trim()
    ) {

      throw new Error(
        `Gemini respondeu sem texto. Eventos recebidos: ${eventosRecebidos}. Bytes: ${bytesRecebidos}. Verifique finishReason/promptFeedback nos logs.`
      );
    }

    console.log(
      `[GEMINI STREAM] Finalizado com sucesso. ${fullText.length} caracteres recebidos.`
    );

    return fullText;

  } catch (err) {

    if (
      timeoutConexao
    ) {

      clearTimeout(
        timeoutConexao
      );

      timeoutConexao =
        null;
    }

    if (
      timeoutInatividade
    ) {

      clearTimeout(
        timeoutInatividade
      );

      timeoutInatividade =
        null;
    }

    if (
      err?.name ===
      "AbortError"
    ) {

      console.error(
        "[GEMINI STREAM] Conexao abortada por timeout."
      );

      throw new Error(
        "Gemini Stream: timeout de conexao ou de inatividade."
      );
    }

    console.error(
      "[GEMINI STREAM] Erro:",
      err.message
    );

    throw err;
  }
}

// ============================================================
// MEMORIA CONTEXTUAL
// ============================================================

function normalizarTexto(
  texto
) {

  if (!texto)
    return "";

  return texto
    .toLowerCase()
    .normalize("NFD")
    .replace(
      /[\u0300-\u036f]/g,
      ""
    )
    .replace(
      /[^\w\s]/g,
      " "
    );
}

function tokenizar(
  texto
) {

  const stopwords =
    new Set([

      "o",
      "a",
      "de",
      "da",
      "do",
      "dos",
      "das",
      "e",
      "que",
      "em",
      "um",
      "uma",
      "para",
      "com",
      "na",
      "no",
      "nas",
      "nos",
      "se",
      "eu",
      "voce",
      "rubens",
      "nyra",
      "isso",
      "aqui",
      "la",
      "me",
      "te",
      "por",
      "mas",
      "ou",
      "ja",
      "ta",
      "ne",
      "oi",
      "ola",
      "ele",
      "ela",
      "seu",
      "sua",
      "muito",
      "bem",
      "sobre",
      "como"

    ]);

  return normalizarTexto(
    texto
  )
    .split(
      /\s+/
    )
    .filter(
      (w) =>
        w.length > 2 &&
        !stopwords.has(w)
    );
}

function ehSaudacaoSimples(
  prompt
) {

  const p =
    normalizarTexto(
      prompt
    ).trim();

  const saudacoes = [

    "oi",
    "ola",
    "e ai",
    "eae",
    "fala",
    "bom dia",
    "boa tarde",
    "boa noite",
    "tudo bem",
    "como vai",
    "hey",
    "salve"

  ];

  if (
    p.length <= 20
  ) {

    return saudacoes.some(
      (s) =>
        p === s ||
        p.startsWith(
          s + " "
        )
    );
  }

  return false;
}

const GATILHOS_MEMORIA = {

  gosto_leitura: [

    "livro",
    "ler",
    "leitura",
    "pagina",
    "autor",
    "capitulo"

  ],

  gosto_filosofia: [

    "filosofia",
    "aristoteles",
    "etica",
    "pensamento",
    "filosofo"

  ],

  formacao: [

    "faculdade",
    "curso",
    "engenharia",
    "estudar",
    "prova",
    "trabalho",
    "facul"

  ],

  interesse_ia: [

    "ia",
    "inteligencia",
    "modelo",
    "gemini",
    "llm",
    "machine",
    "rede"

  ],

  interesse_atual: [

    "nyra",
    "projeto",
    "vtuber",
    "desenvolvendo",
    "codigo"

  ],

  projeto_atual: [

    "nyra",
    "projeto",
    "vtuber",
    "unity",
    "avatar"

  ],

  livro_atual: [

    "livro",
    "lendo",
    "leitura",
    "nicomaco",
    "etica",
    "pagina"

  ],

  autor_favorito: [

    "autor",
    "aristoteles",
    "filosofo",
    "favorito",
    "livro"

  ],

  relacao_familiar: [

    "familia",
    "irma",
    "irmao",
    "mae",
    "pai",
    "parente"

  ],

  rubens_habitos_leitura: [

    "ler",
    "leitura",
    "livro",
    "tempo",
    "habito"

  ],

  rubens_aspiracoes: [

    "sonho",
    "meta",
    "objetivo",
    "futuro",
    "plano",
    "criar"

  ]

};

function scoreRelevanciaMemoria(
  prompt,
  memoriaItem
) {

  const promptNorm =
    normalizarTexto(
      prompt
    );

  const promptTokens =
    new Set(
      tokenizar(
        prompt
      )
    );

  const memoryTokens =
    tokenizar(
      memoriaItem.content
    );

  if (
    memoryTokens.length ===
    0
  ) {

    return 0;
  }

  let matches =
    0;

  for (
    const token
    of memoryTokens
  ) {

    if (
      promptTokens.has(
        token
      )
    ) {

      matches++;
    }
  }

  const gatilhos =
    GATILHOS_MEMORIA[
      memoriaItem.id
    ] || [];

  for (
    const gatilho
    of gatilhos
  ) {

    if (
      promptNorm.includes(
        gatilho
      )
    ) {

      matches += 2;
    }
  }

  return (
    matches /
    memoryTokens.length
  );
}

function memoriaEhSobreRubens(
  item
) {

  if (
    !item ||
    !item.content
  ) {

    return false;
  }

  if (
    MEMORY_IDS_IGNORAR.has(
      item.id
    )
  ) {

    return false;
  }

  const c =
    item.content.toLowerCase();

  if (
    c.startsWith(
      "a nyra"
    ) ||
    c.startsWith(
      "ela acredita"
    ) ||
    c.startsWith(
      "ela tem consciencia"
    ) ||
    c.includes(
      "vtuber prestativa"
    )
  ) {

    return false;
  }

  return true;
}

function selecionarMemoriasRelevantes(
  prompt,
  max = 2
) {

  turnoContador++;

  if (
    ehSaudacaoSimples(
      prompt
    )
  ) {

    return [];
  }

  const idsRecentes =
    new Set(

      memoriaRecentementeUsada
        .filter(
          (x) =>
            turnoContador -
              x.turno <
            5
        )
        .map(
          (x) =>
            x.id
        )

    );

  const candidatas =
    memory
      .filter(
        memoriaEhSobreRubens
      )
      .map(
        (m) => ({

          ...m,

          score:
            scoreRelevanciaMemoria(
              prompt,
              m
            )

        })
      )
      .filter(
        (m) =>
          m.score >=
          0.2
      )
      .sort(
        (a, b) =>
          b.score -
          a.score
      );

  const selecionadas =
    [];

  for (
    const candidata
    of candidatas
  ) {

    if (
      selecionadas.length >=
      max
    ) {

      break;
    }

    if (
      idsRecentes.has(
        candidata.id
      )
    ) {

      continue;
    }

    selecionadas.push(
      candidata
    );
  }

  return selecionadas;
}

function registrarMemoriasUsadas(
  ids
) {

  for (
    const id
    of ids
  ) {

    memoriaRecentementeUsada.push({

      id,

      turno:
        turnoContador

    });
  }

  memoriaRecentementeUsada =
    memoriaRecentementeUsada.filter(
      (x) =>
        turnoContador -
          x.turno <
        12
    );
}

function formatarMemoriaParaSistema(
  memorias
) {

  if (
    !memorias.length
  ) {

    return "";
  }

  const linhas =
    memorias
      .map(
        (m) =>
          `- ${m.content}`
      )
      .join(
        "\n"
      );

  return `

Conhecimento de fundo (use SOMENTE se encaixar naturalmente no assunto atual):
${linhas}

Lembre-se: isso e background. Nao recite. Nao mencione tudo. Seja sutil.`;
}

// ============================================================
// ATUALIZAR MEMORIA
// ============================================================

async function atualizarMemory(
  prompt
) {

  try {

    const contents = [

      {
        role: "user",

        parts: [

          {
            text:
`Analise somente a mensagem abaixo.

Identifique fatos permanentes e uteis sobre Rubens que possam ser lembrados no futuro.

IMPORTANTE:
- Salve APENAS fatos sobre o Rubens (usuario), nunca sobre a Nyra ou instrucoes de personalidade.
- Nao salve informacoes redundantes ou obvias.
- Nao salve cumprimentos, perguntas vagas ou conversa passageira.
- Cada fato deve ser algo que valeria lembrar daqui a semanas.

Se nao houver nenhuma informacao importante, responda exatamente:

[]

Se houver informacoes importantes, responda SOMENTE com JSON valido neste formato:

[
  {
    "id": "identificador",
    "content": "informacao em portugues brasileiro"
  }
]

Nao escreva explicacoes.
Nao escreva markdown.
Nao escreva comentarios.

Mensagem:
${prompt}`
          }

        ]
      }

    ];

    const resultado =
      await chamarGemini(
        contents,
        {
          temperature: 0.1,
          maxOutputTokens: 200
        },
        `
Voce e um sistema de memoria.

Sua unica funcao e retornar JSON valido.

Nunca escreva texto fora do JSON.

Nunca faca analise visivel.

Nunca escreva explicacoes.
`
      );

    if (!resultado) {

      return;
    }

    let texto =
      resultado
        .replace(
          /```json/gi,
          ""
        )
        .replace(
          /```/g,
          ""
        )
        .trim();

    const inicio =
      texto.indexOf(
        "["
      );

    const fim =
      texto.lastIndexOf(
        "]"
      );

    if (
      inicio !== -1 &&
      fim !== -1 &&
      fim > inicio
    ) {

      texto =
        texto.substring(
          inicio,
          fim + 1
        );
    }

    let novasInfos;

    try {

      novasInfos =
        JSON.parse(
          texto
        );

    } catch (err) {

      console.log(
        "Memoria ignorada: resposta nao era JSON valido."
      );

      return;
    }

    if (
      !Array.isArray(
        novasInfos
      )
    ) {

      return;
    }

    let mudou =
      false;

    for (
      const info
      of novasInfos
    ) {

      if (
        !info ||
        typeof info.id !==
          "string" ||
        typeof info.content !==
          "string"
      ) {

        continue;
      }

      const existe =
        memory.some(
          (m) =>
            m.id ===
            info.id
        );

      if (!existe) {

        memory.push(
          info
        );

        mudou =
          true;

        console.log(
          `Memoria nova: ${info.content}`
        );
      }
    }

    if (
      mudou
    ) {

      salvarMemory();
    }

  } catch (err) {

    console.log(
      "Erro na memoria:",
      err.message
    );
  }
}

// ============================================================
// REAGIR
// ============================================================

app.post(
  "/reagir",
  exigirAutenticacao,
  (
    req,
    res
  ) => {

    const prompt =
      req.body?.prompt;

    if (
      !prompt ||
      typeof prompt !==
        "string"
    ) {

      return res
        .status(400)
        .json({

          error:
            "Prompt vazio"

        });
    }

    const reacao =
      gerarReacaoRapida(
        prompt.trim()
      );

    res.json({

      reacao

    });
  }
);

// ============================================================
// VISAO ESPONTANEA
// ============================================================

const VISION_POLL_INTERVAL_MS =
  45000;

let ultimaVisaoTextoEspontanea =
  "";

let comentarioEspontaneoPendente =
  null;

async function cicloVisaoEspontanea() {

  try {

    const dados =
      await capturarVisaoComLock();

    if (!dados) {

      return;
    }

    if (
      dados ===
      ultimaVisaoTextoEspontanea
    ) {

      return;
    }

    ultimaVisaoTextoEspontanea =
      dados;

    const contents = [

      {
        role: "user",

        parts: [

          {
            text:
`Isto e uma descricao do que esta na tela do Rubens agora, capturada automaticamente (ele nao pediu):

${dados}

Se houver algo genuinamente interessante, engracado, ou que valha um comentario espontaneo e curto, escreva SOMENTE essa frase, em portugues brasileiro, como a Nyra falaria.

Se nao houver nada que realmente valha comentar (tela comum, repeticao do que ja foi visto, nada novo), responda EXATAMENTE:

NADA`
          }

        ]
      }

    ];

    const resultado =
      await chamarGemini(
        contents,
        {
          temperature: 0.9,
          maxOutputTokens: 80
        },
        almaNyra +
`
Voce esta observando a tela do Rubens espontaneamente, sem ele ter pedido.

So comente se for realmente algo que valha a pena.

Nao force um comentario se nao houver nada interessante.

Responda SOMENTE com a fala da Nyra, ou a palavra NADA.`
      );

    if (!resultado) {

      return;
    }

    let texto =
      limparTexto(
        resultado
      );

    texto =
      antiEngasgo(
        texto
      );

    if (
      texto &&
      texto
        .trim()
        .toUpperCase() !==
      "NADA"
    ) {

      comentarioEspontaneoPendente = {

        texto,

        timestamp:
          Date.now()

      };

      console.log(
        "Comentario espontaneo gerado:",
        texto
      );
    }

  } catch (err) {

    console.error(
      "Erro no ciclo de visao espontanea:",
      err.message
    );
  }
}

setInterval(
  cicloVisaoEspontanea,
  VISION_POLL_INTERVAL_MS
);

app.get(
  "/vision/comentario",
  exigirAutenticacaoLocalEstrita,
  async (
    req,
    res
  ) => {

    if (
      !comentarioEspontaneoPendente
    ) {

      return res.json({

        comentario:
          null

      });
    }

    const pendente =
      comentarioEspontaneoPendente;

    comentarioEspontaneoPendente =
      null;

    const expressividade =
      await analisarExpressividadeGemini(
        pendente.texto
      );

    const emocaoParaTTS =
      mapearEmocaoParaTTS(
        expressividade.emotion,
        pendente.texto
      );

    res.json({

      comentario:
        pendente.texto,

      emocao:
        emocaoParaTTS,

      intencao:
        expressividade.intent,

      intensidade:
        expressividade.intensity

    });
  }
);

// ============================================================
// PREPARAR CONTEXTO DO CHAT
// ============================================================

async function prepararChat(
  cleanPrompt
) {

  estadoEmocional =
    detectarEmocao(
      cleanPrompt
    );

  const tecnico =
    perguntaTecnica(
      cleanPrompt
    );

  const modoVisao =
    detectarModoVisao(
      cleanPrompt
    );

  let conteudoVisao =
    "";

  if (
    modoVisao
  ) {

    console.log(
      "Nyra olhando a tela..."
    );

    conteudoVisao =
      await capturarVisaoComLock();
  }

  const promessaEmocao =
    analisarEmocaoGemini(
      cleanPrompt
    );

  atualizarMemory(
    cleanPrompt
  ).catch(
    () => {}
  );

  const memoriasRelevantes =
    selecionarMemoriasRelevantes(
      cleanPrompt,
      2
    );

  const blocoMemoriaSistema =
    formatarMemoriaParaSistema(
      memoriasRelevantes
    );

  if (
    memoriasRelevantes.length
  ) {

    console.log(
      "Memorias relevantes neste turno:",
      memoriasRelevantes
        .map(
          (m) =>
            m.id
        )
        .join(
          ", "
        )
    );
  }

  let contexto =
`Rubens disse:

${cleanPrompt}`;

  if (
    conteudoVisao
  ) {

    contexto +=
`

Informacao visual da tela:

${conteudoVisao}`;
  }

  const contents = [

    ...chatHistory
      .slice(-6)
      .map(
        (msg) => ({

          role:
            msg.role,

          parts: [

            {
              text:
                msg.text
            }

          ]

        })
      ),

    {

      role: "user",

      parts: [

        {
          text:
            contexto
        }

      ]

    }

  ];

  let instrucaoExtra =
    "";

  if (
    estadoEmocional ===
    "empatica"
  ) {

    instrucaoExtra =
      "\nSeja acolhedora e paciente.";
  }

  if (
    tecnico
  ) {

    instrucaoExtra =
      "\nSeja tecnicamente precisa e explique de maneira pratica.";
  }

  const systemPrompt =
`${almaNyra}
${blocoMemoriaSistema}

Estado emocional atual do Rubens:
${estadoEmocional}

${instrucaoExtra}

Agora responda a ultima mensagem de Rubens.

Lembre-se:

Sua saida sera enviada diretamente para Rubens.

Escreva SOMENTE a resposta que Nyra falaria.

Nao escreva analise.
Nao escreva opcoes.
Nao escreva explicacoes sobre como respondeu.
Nao escreva ingles.
Nao escreva portugues e ingles juntos.

A resposta deve soar natural quando for transformada em voz.

RESPONDA AGORA EM PORTUGUES BRASILEIRO.
`;

  return {

    contents,

    systemPrompt,

    promessaEmocao,

    memoriasRelevantes,

    contexto,

    tecnico

  };
}

// ============================================================
// FINALIZAR RESPOSTA
// ============================================================

function finalizarFala(
  resposta
) {

  let fala =
    limparTexto(
      resposta
    );

  fala =
    antiEngasgo(
      fala
    );

  if (
    fala.includes(
      "User:"
    ) ||
    fala.includes(
      "Persona:"
    ) ||
    fala.includes(
      "Options:"
    ) ||
    fala.includes(
      "Final choice:"
    )
  ) {

    const partes =
      fala.split(
        /User:|Persona:|Options:|Final choice:/i
      );

    const ultima =
      partes[
        partes.length - 1
      ]?.trim();

    if (
      ultima &&
      ultima.length > 2
    ) {

      fala =
        ultima;
    }
  }

  fala =
    fala
      .replace(
        /^RESPOSTA\s*:\s*/i,
        ""
      )
      .trim();

  return fala;
}

// ============================================================
// CHAT NORMAL
// ============================================================

app.post(
  "/chat",
  exigirAutenticacao,
  limitarTaxa,
  async (
    req,
    res
  ) => {

    try {

      const prompt =
        req.body?.prompt;

      if (
        !prompt ||
        typeof prompt !==
          "string"
      ) {

        return res
          .status(400)
          .json({

            error:
              "Prompt vazio"

          });
      }

      const cleanPrompt =
        prompt.trim();

      const dados =
        await prepararChat(
          cleanPrompt
        );

      console.log(
        "Enviando pergunta para o Gemini..."
      );

      const resposta =
        await chamarGemini(
          dados.contents,
          {

            temperature:
              dados.tecnico
                ? 0.4
                : 0.8,

            maxOutputTokens:
              512

          },
          dados.systemPrompt
        );

      if (!resposta) {

        return res
          .status(502)
          .json({

            error:
              "O modelo nao retornou resposta."

          });
      }

      console.log(
        "\nRESPOSTA BRUTA DO MODELO:\n"
      );

      console.log(
        resposta
      );

      const fala =
        finalizarFala(
          resposta
        );

      if (!fala) {

        return res
          .status(502)
          .json({

            error:
              "A resposta ficou vazia."

          });
      }

      console.log(
        "\nRESPOSTA FINAL DA NYRA:\n"
      );

      console.log(
        fala
      );

      // ======================================================
      // EXPRESSIVIDADE
      // ======================================================

      const expressividade =
        await analisarExpressividadeGemini(
          fala
        );

      const emocaoParaTTS =
        mapearEmocaoParaTTS(
          expressividade.emotion,
          fala
        );

      console.log(
        "Expressividade da Nyra:"
      );

      console.log(
        `   emocao: ${expressividade.emotion}`
      );

      console.log(
        `   intencao: ${expressividade.intent}`
      );

      console.log(
        `   intensidade: ${expressividade.intensity}`
      );

      console.log(
        `   -> TTS: ${emocaoParaTTS}`
      );

      registrarMemoriasUsadas(

        dados
          .memoriasRelevantes
          .filter(
            (m) =>
              m.score >=
              0.35
          )
          .map(
            (m) =>
              m.id
          )

      );

      chatHistory.push(

        {
          role: "user",

          text:
            dados.contexto
        },

        {
          role: "model",

          text:
            fala
        }

      );

      if (
        chatHistory.length >
        12
      ) {

        chatHistory =
          chatHistory.slice(
            -12
          );
      }

      res.json({

        reply:
          fala,

        emocao:
          emocaoParaTTS,

        intencao:
          expressividade.intent,

        intensidade:
          expressividade.intensity

      });

    } catch (err) {

      console.error(
        "Erro na rota /chat:",
        err.message
      );

      res
        .status(500)
        .json({

          error:
            "Erro interno do servidor."

        });
    }
  }
);

// ============================================================
// CHAT STREAMING
// ============================================================

app.post(
  "/chat-stream",
  exigirAutenticacao,
  limitarTaxa,
  async (
    req,
    res
  ) => {

    let respostaCompleta =
      "";

    let contextoChat =
      null;

    try {

      const prompt =
        req.body?.prompt;

      if (
        !prompt ||
        typeof prompt !==
          "string"
      ) {

        return res
          .status(400)
          .json({

            error:
              "Prompt vazio"

          });
      }

      const cleanPrompt =
        prompt.trim();

      console.log(
        "\n================================"
      );

      console.log(
        "NOVO CHAT STREAM"
      );

      console.log(
        "Prompt:",
        cleanPrompt
      );

      console.log(
        "================================"
      );

      contextoChat =
        await prepararChat(
          cleanPrompt
        );

      // ======================================================
      // NDJSON
      // ======================================================

      res.status(200);

      res.setHeader(
        "Content-Type",
        "application/x-ndjson; charset=utf-8"
      );

      res.setHeader(
        "Cache-Control",
        "no-cache, no-transform"
      );

      res.setHeader(
        "Connection",
        "keep-alive"
      );

      if (
        typeof res.flushHeaders ===
        "function"
      ) {

        res.flushHeaders();
      }

      // ======================================================
      // EMOCAO INICIAL
      // ======================================================

      const emocaoInicial =
        mapearEmocaoParaTTS(

          contextoChat.tecnico
            ? "neutra"
            : detectarEmocao(
                cleanPrompt
              ),

          cleanPrompt

        );

      res.write(

        JSON.stringify({

          type:
            "start",

          emocao:
            emocaoInicial,

          intencao:
            "neutral",

          intensidade:
            0.3

        }) +
        "\n"

      );

      // ======================================================
      // GEMINI STREAM
      // ======================================================

      respostaCompleta =
        await chamarGeminiStream(

          contextoChat.contents,

          {

            temperature:
              contextoChat.tecnico
                ? 0.4
                : 0.8,

            maxOutputTokens:
              512

          },

          contextoChat.systemPrompt,

          (chunk) => {

            if (
              !chunk ||
              res.writableEnded
            ) {

              return;
            }

            try {

              res.write(

                JSON.stringify({

                  type:
                    "text",

                  text:
                    chunk

                }) +
                "\n"

              );

            } catch (
              err
            ) {

              console.error(
                "Erro enviando chunk:",
                err.message
              );
            }
          }
        );

      // ======================================================
      // LIMPEZA FINAL
      // ======================================================

      const fala =
        finalizarFala(
          respostaCompleta
        );

      if (!fala) {

        if (
          !res.writableEnded
        ) {

          res.write(

            JSON.stringify({

              type:
                "error",

              error:
                "A resposta ficou vazia."

            }) +
            "\n"

          );

          res.end();
        }

        return;
      }

      console.log(
        "\n[STREAM] RESPOSTA FINAL DA NYRA:\n"
      );

      console.log(
        fala
      );

      // ======================================================
      // EXPRESSIVIDADE FINAL DA NYRA
      // ======================================================

      let expressividade = {

        emotion:
          "neutra",

        intent:
          detectarIntencaoLocal(
            fala
          ),

        intensity:
          estimarIntensidadeLocal(
            fala
          ),

        score:
          0

      };

      try {

        expressividade =
          await analisarExpressividadeGemini(
            fala
          );

      } catch (err) {

        console.log(
          "[STREAM] Falha na expressividade final. Usando fallback."
        );
      }

      const emocaoFinal =
        mapearEmocaoParaTTS(

          expressividade.emotion,

          fala

        );

      console.log(
        `[STREAM] Emocao final: ${emocaoFinal}`
      );

      console.log(
        `[STREAM] Intencao final: ${expressividade.intent}`
      );

      console.log(
        `[STREAM] Intensidade final: ${expressividade.intensity}`
      );

      // ======================================================
      // MEMORIA
      // ======================================================

      registrarMemoriasUsadas(

        contextoChat
          .memoriasRelevantes
          .filter(
            (m) =>
              m.score >=
              0.35
          )
          .map(
            (m) =>
              m.id
          )

      );

      // ======================================================
      // HISTORICO
      // ======================================================

      chatHistory.push(

        {
          role: "user",

          text:
            contextoChat.contexto
        },

        {
          role: "model",

          text:
            fala
        }

      );

      if (
        chatHistory.length >
        12
      ) {

        chatHistory =
          chatHistory.slice(
            -12
          );
      }

      // ======================================================
      // DONE
      // ======================================================

      if (
        !res.writableEnded
      ) {

        // ----------------------------------------------------
        // EVENTO EMOTION
        // ----------------------------------------------------

        res.write(

          JSON.stringify({

            type:
              "emotion",

            emocao:
              emocaoFinal,

            intencao:
              expressividade.intent,

            intensidade:
              expressividade.intensity

          }) +
          "\n"

        );

        // ----------------------------------------------------
        // EVENTO DONE
        // ----------------------------------------------------

        res.write(

          JSON.stringify({

            type:
              "done",

            reply:
              fala,

            emocao:
              emocaoFinal,

            intencao:
              expressividade.intent,

            intensidade:
              expressividade.intensity

          }) +
          "\n"

        );

        res.end();
      }

      console.log(
        "[CHAT STREAM] Concluido."
      );

    } catch (err) {

      console.error(
        "[CHAT STREAM] Erro:",
        err.message
      );

      if (
        !res.headersSent
      ) {

        return res
          .status(500)
          .json({

            error:
              "Erro interno do servidor."

          });
      }

      if (
        !res.writableEnded
      ) {

        try {

          res.write(

            JSON.stringify({

              type:
                "error",

              error:
                err.message ||
                "Erro no streaming."

            }) +
            "\n"

          );

          res.end();

        } catch {}
      }
    }
  }
);

// ============================================================
// START
// ============================================================

app.listen(
  PORT,
  () => {

    console.log(
      `Nyra Online na porta ${PORT}`
    );

    console.log(
      `Modelo: ${GEMINI_MODEL}`
    );

    console.log(
      `Emotion API: ${EMOTION_API_URL}`
    );

    console.log(
      `Visao espontanea: a cada ${VISION_POLL_INTERVAL_MS / 1000}s`
    );

    console.log(
      "Chat Streaming: ATIVO"
    );

    console.log(
      "Expressividade V5: ATIVA"
    );

    console.log(
      "Intencao vocal: ATIVA"
    );

    console.log(
      "Intensidade vocal: ATIVA"
    );

    console.log(
      "Idioma: PT-BR"
    );

  }
);
