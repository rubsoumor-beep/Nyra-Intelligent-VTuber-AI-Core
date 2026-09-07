import time
import os
import signal
import concurrent.futures
import pyautogui
import google.generativeai as genai
import io
import pyperclip


# ============================================================
# 🔐 CONFIGURAÇÃO DE API
# ============================================================
#
# As chaves NÃO devem ficar neste arquivo.
#
# Configure no ambiente:
#
# Windows PowerShell:
#   $env:GEMINI_API_KEYS="CHAVE_1,CHAVE_2"
#
# Ou utilize um arquivo .env local que esteja no .gitignore.
#

CHAVES_API = [
    key.strip()
    for key in os.getenv("GEMINI_API_KEYS", "").split(",")
    if key.strip()
]


# ============================================================
# 🤖 MODELO
# ============================================================

MODELO_LITE = os.getenv(
    "GEMINI_VISION_MODEL",
    "gemini-3.1-flash-lite"
)


# ============================================================
# 📂 PASTA DA VISÃO
# ============================================================
#
# O diretório pode ser configurado através da variável
# NYRA_VISION_DIR.
#
# Caso não seja configurado, utiliza uma pasta local
# chamada "vision_data".
#

DIRETORIO_VISAO = os.getenv(
    "NYRA_VISION_DIR",
    os.path.join(
        os.path.dirname(
            os.path.abspath(__file__)
        ),
        "vision_data"
    )
)

CAMINHO_VISAO = os.path.join(
    DIRETORIO_VISAO,
    "visao.txt"
)

CAMINHO_GATILHO = os.path.join(
    DIRETORIO_VISAO,
    "trigger.txt"
)


# ============================================================
# 📊 CONTROLE
# ============================================================

cotas_acumuladas = 0


# ============================================================
# 🔍 DEBUG DOS CAMINHOS
# ============================================================

print("")
print("============================================================")
print("🐉 NYRA - SISTEMA DE VISÃO")
print("============================================================")
print("📂 Pasta da visão:")
print(DIRETORIO_VISAO)
print("")
print("👁 Arquivo de visão:")
print(CAMINHO_VISAO)
print("")
print("⚡ Arquivo de gatilho:")
print(CAMINHO_GATILHO)
print("============================================================")
print("")


# ============================================================
# 📁 GARANTIR QUE A PASTA EXISTE
# ============================================================

try:

    os.makedirs(
        DIRETORIO_VISAO,
        exist_ok=True
    )

    print("✅ Pasta da visão verificada.")

except Exception as e:

    print(
        "❌ Não foi possível acessar a pasta da visão:"
    )

    print(e)


# ============================================================
# 📝 ESCREVER VISÃO
# ============================================================

def escrever_visao(texto):

    try:

        caminho_temporario = os.path.join(
            DIRETORIO_VISAO,
            "visao_temp.txt"
        )

        with open(
            caminho_temporario,
            "w",
            encoding="utf-8"
        ) as f:

            f.write(texto)
            f.flush()
            os.fsync(f.fileno())


        if os.path.exists(CAMINHO_VISAO):

            try:
                os.remove(CAMINHO_VISAO)

            except:
                pass


        os.replace(
            caminho_temporario,
            CAMINHO_VISAO
        )


        print("💾 visao.txt atualizado.")


    except Exception as e:

        print(
            "❌ Erro ao escrever visão:",
            e
        )


# ============================================================
# 🤖 EXECUTAR GEMINI
# ============================================================

def _chamar_gemini_bloqueante(
    chave,
    nome_modelo,
    img_bytes,
    prompt
):

    genai.configure(
        api_key=chave
    )

    model = genai.GenerativeModel(
        nome_modelo
    )

    response = model.generate_content(

        [
            prompt,

            {
                "mime_type": "image/webp",
                "data": img_bytes
            }

        ]

    )

    if response and hasattr(
        response,
        "text"
    ):

        texto = response.text.strip()

        if texto:

            return texto

    return None


def executar_modelo(
    chave,
    nome_modelo,
    img_bytes,
    prompt
):

    try:

        executor = concurrent.futures.ThreadPoolExecutor(
            max_workers=1
        )

        future = executor.submit(
            _chamar_gemini_bloqueante,
            chave,
            nome_modelo,
            img_bytes,
            prompt
        )

        try:

            resultado = future.result(
                timeout=15
            )

            executor.shutdown(
                wait=False
            )

            return resultado

        except concurrent.futures.TimeoutError:

            print(
                "⚠️ Timeout: sem resposta em 15s.",
                flush=True
            )

            executor.shutdown(
                wait=False
            )

            return None


    except Exception as e:

        print(
            f"⚠️ Erro nesta chave "
            f"[{type(e).__name__}]: {e}",
            flush=True
        )


    return None


# ============================================================
# 👁 PROCESSAR VISÃO
# ============================================================

def processar_olhar():

    global cotas_acumuladas

    inicio_trigger = time.time()


    try:

        # ====================================================
        # 📋 CAPTURA DE TEXTO SELECIONADO
        # ====================================================

        try:

            original_clip = pyperclip.paste()

        except:

            original_clip = ""


        try:

            handler_original = signal.signal(
                signal.SIGINT,
                signal.SIG_IGN
            )

            try:

                pyautogui.hotkey(
                    "ctrl",
                    "c"
                )

                time.sleep(0.2)

            finally:

                signal.signal(
                    signal.SIGINT,
                    handler_original
                )

            texto_selecionado = (
                pyperclip.paste()
                .strip()
            )

        except Exception:

            texto_selecionado = ""


        foi_selecionado = (

            texto_selecionado !=
            original_clip

            and

            texto_selecionado != ""

        )


        if foi_selecionado:

            print(
                "🎯 TEXTO SELECIONADO DETECTADO:"
            )

            print(
                texto_selecionado[:100]
            )


            foco_msg = (
                "O usuário selecionou este texto: "
                f"'{texto_selecionado}'. "
                "Foque sua análise nele."
            )

        else:

            print(
                "📸 SEM SELEÇÃO: analisando visão geral."
            )

            foco_msg = (
                "Não há texto selecionado. "
                "Analise a tela de forma geral."
            )


        # ====================================================
        # 📸 CAPTURA DE TELA
        # ====================================================

        print(
            "📸 Capturando tela..."
        )


        screenshot = pyautogui.screenshot()


        screenshot.thumbnail(
            (
                1280,
                720
            )
        )


        # ====================================================
        # 🖼 CONVERTER PARA WEBP
        # ====================================================

        img_byte_arr = io.BytesIO()


        screenshot.save(
            img_byte_arr,
            format="WEBP",
            quality=40
        )


        img_bytes = (
            img_byte_arr.getvalue()
        )


        print(
            f"📦 Imagem preparada: "
            f"{len(img_bytes)} bytes"
        )


        # ====================================================
        # 🧠 PROMPT
        # ====================================================
        #
        # Mantido como prompt genérico para a versão pública.
        # A versão proprietária pode permanecer no ambiente privado.
        #

        prompt = f"""

Analise esta tela como uma Desenvolvedora Senior.

{foco_msg}

IMPORTANTE:

Se houver um texto selecionado acima,
priorize a explicação dele.

Não invente código ou contexto
que não esteja na tela.

Se algo não estiver legível na imagem,
use o texto selecionado como fonte principal.


ETAPA 1 — TRANSCRIÇÃO

Transcreva exatamente as linhas principais
ou confirme o texto selecionado.


ETAPA 2 — ANÁLISE

1. SE FOR CÓDIGO:

- Identifique o arquivo.
- Explique a lógica das linhas visíveis.
- O código é C# quando aplicável.
- Seja objetiva.

2. SE FOR IMAGEM:

- Descreva elementos.
- Descreva cores.
- Descreva posições.
- Descreva ícones.

3. ERROS:

- Procure sublinhados vermelhos.
- Procure warnings.
- Procure mensagens de erro.
- Procure bugs visíveis no console.

Seja técnica e objetiva.

"""


        # ====================================================
        # 🔑 TENTAR CHAVES CONFIGURADAS
        # ====================================================

        if not CHAVES_API:

            print(
                "❌ Nenhuma chave Gemini configurada."
            )

            return False


        cotas_deste_tiro = 0


        for i, chave in enumerate(
            CHAVES_API
        ):

            id_real = i + 1

            print("")
            print(
                f"🎯 Tentando credencial {id_real}..."
            )


            cotas_deste_tiro += 1


            resultado = executar_modelo(

                chave,
                MODELO_LITE,
                img_bytes,
                prompt

            )


            if resultado:

                tempo_total = (
                    time.time()
                    - inicio_trigger
                )


                cotas_acumuladas += (
                    cotas_deste_tiro
                )


                print("")
                print(
                    f"✅ Sucesso com credencial "
                    f"{id_real}."
                )


                print(
                    f"⏱️ Tempo: {tempo_total:.2f}s"
                )


                print(
                    f"📊 Tentativas: "
                    f"{cotas_deste_tiro}"
                )


                print("")


                escrever_visao(
                    resultado
                )


                if os.path.exists(
                    CAMINHO_VISAO
                ):

                    tamanho = os.path.getsize(
                        CAMINHO_VISAO
                    )

                    print(
                        f"✅ visao.txt criado: "
                        f"{tamanho} bytes"
                    )

                else:

                    print(
                        "❌ ERRO: visao.txt não foi criado."
                    )


                return True


            else:

                print(
                    f"⚠️ Credencial {id_real} falhou."
                )


        print("")
        print(
            "❌ Nenhuma credencial respondeu."
        )


        return False


    except Exception as e:

        print(
            "❌ ERRO GERAL NA VISÃO:"
        )

        print(e)

        return False


# ============================================================
# 🧹 LIMPAR ARQUIVOS ANTIGOS
# ============================================================

try:

    if os.path.exists(
        CAMINHO_GATILHO
    ):

        os.remove(
            CAMINHO_GATILHO
        )

        print(
            "🧹 trigger.txt antigo removido."
        )


except Exception as e:

    print(
        "⚠️ Não foi possível remover trigger antigo:",
        e
    )


# ============================================================
# 🚀 INICIAR
# ============================================================

print("")
print(
    "🐉 MODO VISÃO ATIVO."
)

print(
    "👁 Aguardando trigger.txt..."
)

print("")


# ============================================================
# 🔄 LOOP PRINCIPAL
# ============================================================

while True:

    try:

        if os.path.exists(
            CAMINHO_GATILHO
        ):

            print("")
            print(
                "⚡ GATILHO RECEBIDO!"
            )


            try:

                os.remove(
                    CAMINHO_GATILHO
                )

                print(
                    "🗑 trigger.txt removido."
                )

            except Exception as e:

                print(
                    "⚠️ Não foi possível remover trigger:",
                    e
                )


            sucesso = processar_olhar()


            if sucesso:

                print(
                    "✅ Visão concluída."
                )

            else:

                print(
                    "❌ Visão falhou."
                )


            print("")
            print(
                "👁 Aguardando próximo trigger..."
            )


            time.sleep(
                0.5
            )


        time.sleep(
            0.1
        )


    except KeyboardInterrupt:

        print("")
        print(
            "🛑 Sistema de visão encerrado."
        )

        break


    except Exception as e:

        print(
            "❌ Erro no loop principal:",
            e
        )

        time.sleep(
            1
        )