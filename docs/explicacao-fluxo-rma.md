# Explicação do Fluxo RMA

## Visão geral

O projeto é um Worker Service em .NET 8 para automatizar a triagem inicial de solicitações de RMA recebidas por e-mail.

O fluxo principal é:

```text
RmaEmailWorker
 -> GmailService busca e-mails
 -> RmaProcessorService processa cada e-mail
 -> EmailBodyCleaner limpa histórico da thread
 -> OllamaService extrai RMAs do texto
 -> CnpjValidator valida CNPJ
 -> SerialValidationService consulta UNO
 -> RmaTechnicalClassifier decide se libera ou pede testes
 -> InvoicePdfService lê PDF da nota
 -> EmailResponseService monta resposta
 -> GmailService envia resposta e marca thread como processada
```

O ponto principal do desenho é: a IA apenas extrai dados. As decisões de negócio ficam no backend em C#.

## Program.cs

Arquivo:

```text
RmaWorker/Program.cs
```

É o ponto de entrada da aplicação.

Ele registra:

- configurações do `appsettings.json`;
- serviços do Gmail;
- serviço do Ollama;
- consulta de serial no UNO;
- leitura de PDF;
- validação de CNPJ;
- limpeza de corpo de e-mail;
- classificador técnico;
- processador principal;
- worker em background.

No final, registra:

```csharp
builder.Services.AddHostedService<RmaEmailWorker>();
```

Isso faz o worker iniciar junto com a aplicação.

## RmaEmailWorker

Arquivo:

```text
RmaWorker/Workers/RmaEmailWorker.cs
```

É o loop principal.

Ele:

1. busca e-mails não lidos;
2. processa cada mensagem;
3. marca a thread como processada;
4. espera o intervalo configurado;
5. repete.

A busca atual vem do `appsettings.json`:

```json
"SearchQuery": "is:unread subject:RMA_TESTE -label:\"RMA PROCESSADO\""
```

Ou seja, em dev ele busca e-mails não lidos com assunto `RMA_TESTE`, exceto os já marcados com `RMA PROCESSADO`.

## GmailService

Arquivo:

```text
RmaWorker/Services/GmailService.cs
```

Responsável por falar com a API do Gmail.

Funções principais:

- autenticar usando `credentials.json` e `token.json`;
- buscar mensagens não lidas;
- extrair corpo da mensagem;
- enviar resposta em texto puro;
- enviar resposta em HTML;
- aplicar label de processado.

Escopos usados:

```text
GmailModify
GmailSend
```

Ao enviar resposta, o serviço mantém o `ThreadId`, então a resposta entra na mesma conversa.

Ao marcar como processado, o serviço:

- aplica `RMA PROCESSADO` na thread;
- remove `UNREAD` da thread.

Isso evita que o worker processe novamente outra mensagem não lida da mesma conversa, inclusive respostas do próprio sistema.

## EmailBodyCleaner

Arquivo:

```text
RmaWorker/Services/EmailBodyCleaner.cs
```

Responsável por limpar o corpo do e-mail antes da extração pela IA.

Isso é necessário porque o Gmail pode entregar a conversa com histórico, respostas antigas ou mensagens encaminhadas.

O cleaner corta o texto quando encontra marcadores como:

```text
---------- Forwarded message ---------
De:
From:
Em ... escreveu:
On ... wrote:
Segue informações para abertura do RMA de manutenção
Recebemos a solicitação de RMA...
```

Se depois da limpeza o corpo ficar vazio, o processador ignora a mensagem. Isso evita responder em cima de um template que o próprio sistema enviou.

## OllamaService

Arquivo:

```text
RmaWorker/Services/OllamaService.cs
```

Responsável por chamar o Ollama local.

Endpoint:

```text
POST /api/generate
```

Modelo atual:

```text
qwen3:4b
```

O serviço envia um prompt pedindo JSON válido no formato:

```json
{
  "rmas": [
    {
      "serial": "0M0200/013D88",
      "cnpj": "36045173000173",
      "defeito": "Não liga mais",
      "produto": "iDFace",
      "garantiaInformada": "GARANTIA",
      "evidenciasInformadas": false,
      "testesInformados": false,
      "possuiSerial": true,
      "possuiCnpj": true,
      "possuiDefeito": true
    }
  ]
}
```

Cada item em `rmas` representa uma solicitação/equipamento.

O serviço também trata um comportamento do `qwen3:4b`: às vezes o JSON vem no campo `thinking` em vez de `response`.

## Normalização da extração

Depois que a IA responde, o backend normaliza os dados.

Regras atuais:

- CNPJ fica apenas com números.
- Defeito só é aceito se o texto existir no corpo do e-mail.
- Serial vindo da IA também é validado.
- Se a IA não extrair todos os seriais, o fallback por regex tenta encontrar.

Formatos de serial aceitos:

```text
0X0X00/00XXXX
```

Exemplos:

```text
0M0200/013D88
0X0200/004245
```

Formato antigo raro:

```text
L249
```

Também há bloqueio para falsos positivos como:

```text
EQUIPAMENTO
SERIE
NOTA
PRODUTO
NCM
CFOP
```

## DTOs principais

Pasta:

```text
RmaWorker/DTOs
```

Principais DTOs:

```text
OllamaRmaExtractionDto
```

Representa cada item extraído pela IA.

```text
RmaExtractionResultDto
```

Representa o JSON raiz com a lista `rmas`.

```text
RmaProcessingResultDto
```

Representa o resultado do processamento de um item.

Ele guarda:

- extração original;
- status;
- motivo;
- campos faltantes;
- classificação técnica;
- dados do UNO;
- dados da nota;
- garantia.

## RmaProcessorService

Arquivo:

```text
RmaWorker/Services/RmaProcessorService.cs
```

É o centro do fluxo.

Para cada e-mail, ele:

1. imprime/loga o conteúdo;
2. limpa o corpo com `EmailBodyCleaner`;
3. ignora se o corpo atual ficar vazio;
4. chama o `OllamaService`;
5. processa cada item extraído;
6. envia resposta consolidada.

Para cada item, ele:

1. valida campos obrigatórios;
2. valida CNPJ;
3. consulta serial no UNO;
4. classifica tecnicamente;
5. se estiver liberado, lê dados da nota;
6. calcula garantia;
7. monta resultado.

Statuses atuais:

```text
DADOS_AUSENTES
CNPJ_INVALIDO
SERIAL_NAO_ENCONTRADO
PRECISA_TESTES
APTO
```

## Campos obrigatórios

Para a triagem, são obrigatórios:

- serial;
- CNPJ;
- defeito.

Se faltar algum, o item vira:

```text
DADOS_AUSENTES
```

Se o CNPJ não passar na validação básica:

```text
CNPJ_INVALIDO
```

## CnpjValidator

Arquivo:

```text
RmaWorker/Validators/CnpjValidator.cs
```

Faz validação local básica do CNPJ.

Hoje ele não consulta base externa.

## SerialValidationService

Arquivo:

```text
RmaWorker/Services/SerialValidationService.cs
```

Consulta o UNO pelo serial.

URL:

```text
http://uno.controlid.com.br/supplychain/consultar.sh?<serial>
```

Importante:

```text
A barra "/" do serial deve ir crua na query.
Não pode virar "%2F".
```

O serviço extrai:

- serial;
- código do produto;
- descrição do produto;
- pedido UNO;
- link da nota fiscal;
- razão social;
- CNPJ;
- data de emissão;
- cidade;
- CEP.

Se o serial não existir no UNO, o item vira:

```text
SERIAL_NAO_ENCONTRADO
```

## RmaTechnicalClassifier

Arquivo:

```text
RmaWorker/Services/RmaTechnicalClassifier.cs
```

Decide se o item já pode receber orientação de nota ou se precisa de testes/evidências.

Libera orientação de nota quando:

- defeito contém `não liga`;
- defeito contém `queimado`;
- defeito contém `queimada`;
- defeito contém `sem sinal de vida`;
- o e-mail informa anexos, vídeos, evidências, testes, atualização, reset, recovery ou defeito persistente.

Pede testes quando encontra:

- reiniciando;
- funcionamento esporádico;
- intermitente;
- falha de TAG/cartão/leitura;
- falha de comunicação;
- falha de reconhecimento;
- travamento;
- lentidão;
- listras/tela;
- defeitos genéricos sem evidência.

Se precisa de testes, o item vira:

```text
PRECISA_TESTES
```

Nesse caso, o template de orientação de nota não é enviado para aquele item.

## InvoicePdfService

Arquivo:

```text
RmaWorker/Services/InvoicePdfService.cs
```

Baixa a DANFE em PDF usando o link retornado pelo UNO.

Usa PdfPig para extrair texto do PDF.

Extrai:

- número da nota;
- data da nota;
- NCM;
- valor unitário.

O parser cruza os dados pelo `ProductCode` retornado pelo UNO para achar o produto correto dentro da nota.

## Garantia

O cálculo é feito no `RmaProcessorService`.

Regra:

```text
data da nota + 1 ano >= data atual
```

Se sim:

```text
isUnderWarranty = true
```

Se não:

```text
isUnderWarranty = false
```

Mas fora de garantia não bloqueia RMA. A informação é registrada, mas o item pode seguir para orientação de nota.

## EmailResponseService

Arquivo:

```text
RmaWorker/Services/EmailResponseService.cs
```

Monta as respostas enviadas ao cliente.

Tipos de resposta:

- dados faltantes;
- serial não encontrado;
- itens que precisam de testes/evidências;
- apto para RMA.

O template de RMA apto é HTML para preservar:

- textos vermelhos;
- link azul;
- observações com fundo amarelo;
- estrutura visual do modelo usado pela equipe.

Quando há várias RMAs no mesmo e-mail:

- itens `APTO` entram no template de orientação de nota;
- itens pendentes aparecem no final, em uma seção de correções/testes.

Se nenhum item estiver apto, o sistema envia uma resposta textual consolidada.

## Exemplo de fluxo simples

E-mail:

```text
Segue CNPJ 36045173000173, NS: 0M0200/013D88, Defeito: Não liga mais
```

Fluxo:

1. Gmail encontra e-mail não lido.
2. Cleaner mantém o corpo.
3. Ollama extrai uma RMA.
4. Backend valida serial, CNPJ e defeito.
5. UNO encontra o serial.
6. Classificador libera porque o defeito é `não liga`.
7. Sistema baixa/lê a nota.
8. Calcula garantia.
9. Envia template HTML de orientação de nota.
10. Marca a thread como processada e lida.

## Exemplo com várias RMAs

E-mail com vários equipamentos:

```text
0F0200/0003E9 - não liga
0L0100/000FE6 - fica reiniciando
0G0210/005B74 - falha ao ler TAGs
```

Resultado esperado:

- `0F0200/0003E9`: liberado para orientação de nota;
- `0L0100/000FE6`: precisa de testes;
- `0G0210/005B74`: precisa de testes.

Resposta:

- template de nota para o item liberado;
- seção final pedindo testes/evidências para os demais.

## Pontos de atenção

Ainda há pendências importantes:

- persistir histórico por `threadId + serial`;
- evitar duplicidade de forma mais robusta;
- criar testes automatizados;
- melhorar validação de CNPJ consultando base do UNO futuramente;
- revisar logs com dados sensíveis antes de produção.

Sem persistência, o sistema já reduz bastante reprocessamento usando label, remoção de `UNREAD` e limpeza de corpo, mas ainda não tem memória histórica formal por serial/thread.
