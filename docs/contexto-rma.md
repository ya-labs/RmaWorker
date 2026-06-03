# Contexto do Projeto RMA

## Estado Atual

Projeto em desenvolvimento para automatizar a triagem inicial de solicitações de RMA recebidas por e-mail.

O sistema não abre RMA automaticamente. Ele:

- lê e-mails no Gmail;
- limpa o corpo para processar apenas a mensagem nova, sem histórico de thread;
- extrai dados do texto com IA local via Ollama;
- valida informações obrigatórias no backend;
- consulta o serial no UNO;
- classifica tecnicamente a solicitação;
- extrai dados da nota fiscal em PDF quando o item está liberado para orientação de nota;
- responde o e-mail automaticamente;
- aplica label `RMA PROCESSADO` e remove `UNREAD` da thread.

## Regras de Negócio

A IA não toma decisão de negócio. Ela apenas extrai dados.

O backend decide:

- se falta serial, CNPJ ou defeito;
- se o CNPJ tem formato básico válido;
- se o serial existe no UNO;
- se a solicitação precisa de testes/evidências antes da orientação de nota;
- se o produto está dentro da garantia;
- qual resposta deve ser enviada.

Campos obrigatórios:

- serial;
- CNPJ;
- descrição do defeito.

Produtos fora da garantia também seguem para abertura de RMA. A informação de garantia é calculada e registrada, mas não bloqueia o template de RMA apto.

## Fluxo Principal

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

## Configuração Atual

Arquivo:

```text
RmaWorker/appsettings.json
```

Query atual do Gmail:

```json
"SearchQuery": "is:unread subject:RMA_TESTE -label:\"RMA PROCESSADO\""
```

Para testar, os assuntos devem começar com:

```text
RMA_TESTE
```

Modelo Ollama:

```json
"Model": "qwen3:4b"
```

## Gmail

Serviço:

```text
RmaWorker/Services/GmailService.cs
```

Responsabilidades:

- autenticar com Gmail API;
- buscar mensagens não lidas;
- extrair corpo do e-mail;
- enviar respostas em `text/plain` ou `text/html`;
- aplicar label `RMA PROCESSADO`;
- remover `UNREAD` da thread.

Escopos usados:

- `GmailModify`;
- `GmailSend`.

Se mudar escopo ou der erro 403, apagar token e autenticar de novo:

```powershell
Remove-Item -Recurse -Force .\RmaWorker\token.json
dotnet run --project RmaWorker
```

## Limpeza do Corpo do E-mail

Serviço:

```text
RmaWorker/Services/EmailBodyCleaner.cs
```

Remove histórico de respostas e mensagens encaminhadas antes da extração pela IA.

Marcadores tratados:

- `---------- Forwarded message ---------`;
- linhas iniciadas com `De:`;
- linhas iniciadas com `From:`;
- `Em ... escreveu:`;
- `On ... wrote:`;
- `Segue informações para abertura do RMA de manutenção`;
- `Recebemos a solicitação de RMA...`.

Se depois da limpeza o corpo ficar vazio, o processador ignora a mensagem. Isso evita responder em cima do próprio template.

## Extração com Ollama

Serviço:

```text
RmaWorker/Services/OllamaService.cs
```

O Ollama deve retornar:

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

Observação: o `qwen3:4b` às vezes retorna JSON no campo `thinking` em vez de `response`. O `OllamaService` já usa `thinking` como fallback.

## Normalização e Fallback de Extração

O backend normaliza e protege contra erro da IA:

- CNPJ fica só com números;
- defeito só é aceito se o texto aparece no corpo do e-mail;
- serial vindo da IA também é validado;
- se a IA retorna item incompleto, o fallback estruturado pode substituir/completar;
- se a IA não retorna todos os seriais, o backend tenta extrair por regex e por estrutura de linhas.

Formatos de serial aceitos:

- novo: `0X0X00/XXXXXX`, exemplo `0M0200/013D88` e `0X0200/004245`;
- antigo raro: 4 caracteres alfanuméricos, exemplo `L249`.

Falsos positivos bloqueados:

- `EQUIPAMENTO`;
- `SERIE`;
- `SERIAL`;
- `NOTA`;
- `PRODUTO`;
- `NCM`;
- `CFOP`.

O parser estruturado lê blocos como:

```text
SÉRIE: 0M0200/013D88
DEFEITO: Não liga mais
PRODUTO: iDFace
```

E também tabelas simples como:

```text
GARANTIA
0M0200/013D88
não liga, está queimada
LEITOR FACIAL ...
```

## Múltiplas RMAs no Mesmo E-mail

O sistema aceita mais de uma solicitação de RMA no mesmo e-mail.

O `RmaProcessorService` processa cada item individualmente e o `EmailResponseService` envia uma única resposta consolidada.

Se houver itens aptos e itens pendentes:

- itens aptos entram no template de orientação de nota;
- itens pendentes aparecem em uma seção final de correção/testes.

Se todos os itens precisam de testes:

- não envia template de nota;
- envia resposta textual consolidada pedindo testes/evidências.

## Triagem Técnica

Serviço:

```text
RmaWorker/Services/RmaTechnicalClassifier.cs
```

Libera para orientação de nota:

- `não liga`;
- `queimado`;
- `queimada`;
- `sem sinal de vida`;
- e-mails que informam vídeos, anexos, evidências, testes realizados, atualização, reset, recovery ou defeito persistente.

Pede testes/evidências:

- reiniciando;
- funcionamento esporádico/intermitente;
- falha de TAG/cartão/leitura;
- falha de comunicação/reconhecimento;
- travamento/lentidão;
- listras/tela;
- defeitos genéricos sem evidência técnica.

## UNO

Serviço:

```text
RmaWorker/Services/SerialValidationService.cs
```

Consulta:

```text
http://uno.controlid.com.br/supplychain/consultar.sh?<serial>
```

Importante: a barra `/` do serial precisa ir crua na query. Não pode virar `%2F`.

Campos extraídos:

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

## PDF da Nota

Serviço:

```text
RmaWorker/Services/InvoicePdfService.cs
```

Usa PdfPig para ler DANFE em PDF.

Extrai:

- número da nota;
- data da nota;
- NCM;
- valor unitário.

O parser cruza pelo `ProductCode` retornado pelo UNO.

## Garantia

Regra:

```text
data da nota + 1 ano >= data atual => em garantia
```

Fora de garantia não bloqueia abertura de RMA.

## Templates

Serviço:

```text
RmaWorker/Services/EmailResponseService.cs
```

Templates implementados:

- dados faltantes;
- serial não encontrado;
- precisa de testes/evidências;
- apto para RMA em HTML.

O template apto segue o modelo da imagem:

```text
RmaWorker/temp/rma-template.png
```

O template HTML usa:

- vermelho em avisos;
- link azul para `rma-notas@controlid.com.br`;
- fundo amarelo nas observações importantes;
- separador por RMA apta quando houver mais de uma.

Quando há múltiplas RMAs aptas, cada bloco aparece assim:

```text
------------------------------------
RMA 1 - Série 0M0200/013D88
------------------------------------
```

As cores alternam por bloco:

- RMA 1: vermelho;
- RMA 2: azul;
- RMA 3: amarelo/ocre;
- RMA 4: verde;
- RMA 5: roxo;
- depois repete.

## Testes Manuais

Roteiro pronto para apresentação:

```text
docs/roteiro-testes-rma.md
```

Esse arquivo contém 15 cenários com:

- assunto do e-mail;
- corpo para copiar e colar;
- resultado esperado.

Cenários cobertos:

- apto simples;
- dados faltantes;
- CNPJ inválido;
- precisa de testes;
- libera por evidência/anexo;
- múltiplas RMAs;
- serial não encontrado;
- falso positivo `EQUIPAMENTO`;
- serial antigo;
- tabela de revenda;
- evidências enviadas depois.

## Testes Já Observados em Dev

Funcionou:

- `0M0200/013D88` consultado no UNO;
- `0X0200/004245` consultado no UNO;
- `L249` aceito como serial legado e consultado no UNO;
- falso positivo `EQUIPAMENTO` não virou serial;
- serial inexistente `0A0A00/00AAAA` retornou `SERIAL_NAO_ENCONTRADO`;
- múltiplas RMAs com duas aptas processaram os dois seriais;
- resposta consolidada foi enviada na mesma thread;
- label `RMA PROCESSADO` aplicada e `UNREAD` removido da thread.

Ajuste recente importante:

- o padrão do serial novo foi corrigido para aceitar `0X0X00/XXXXXX`, não apenas `0X0X00/00XXXX`;
- o parser linha-a-linha foi adicionado para melhorar blocos `SÉRIE/DEFEITO/PRODUTO`;
- o fallback estruturado substitui extrações incompletas da IA quando encontra dados melhores no corpo.

## Comandos Úteis

Build:

```powershell
dotnet build
```

Rodar worker:

```powershell
dotnet run --project RmaWorker
```

Build sem brigar com worker rodando:

```powershell
dotnet build RmaWorker\RmaWorker.csproj -o .\artifacts\build-check
```

## Pendências

- Persistir histórico por `threadId + serial`.
- Evitar duplicidade de forma mais robusta.
- Melhorar validação de CNPJ consultando base do UNO futuramente.
- Adicionar testes automatizados para:
  - limpeza de corpo de e-mail;
  - parser estruturado de RMAs;
  - classificador técnico;
  - parser do UNO;
  - parser de PDF;
  - validação de CNPJ;
  - cálculo de garantia;
  - templates de e-mail;
  - múltiplas RMAs no mesmo e-mail.
- Avaliar remover logs com dados sensíveis em produção.

## Como Retomar Amanhã

Enviar para o Codex:

```text
Leia docs/contexto-rma.md e retome daqui.
```

Arquivos principais para olhar primeiro:

- `docs/roteiro-testes-rma.md`;
- `RmaWorker/Services/OllamaService.cs`;
- `RmaWorker/Services/RmaProcessorService.cs`;
- `RmaWorker/Services/EmailResponseService.cs`;
- `RmaWorker/Services/RmaTechnicalClassifier.cs`;
- `RmaWorker/Services/GmailService.cs`.
