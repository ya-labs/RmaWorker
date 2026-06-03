# Roteiro de Testes do Worker RMA

## Resumo do Fluxo Para Apresentação

O app é um Worker Service em .NET que monitora uma caixa Gmail em busca de solicitações de RMA.

Fluxo resumido:

1. O worker busca e-mails não lidos com assunto contendo `RMA_TESTE`. (por enquanto)
2. O Gmail retorna a mensagem e o app lê o corpo do e-mail.
3. O app limpa histórico da thread para evitar processar respostas antigas ou o próprio template já enviado.
4. O texto limpo é enviado para o Ollama local.
5. A IA apenas extrai dados, como serial, CNPJ, defeito, produto e evidências. Ela não decide regra de negócio.
6. O backend valida se existem serial, CNPJ e defeito.
7. O backend valida o formato do CNPJ.
8. O backend consulta o serial no UNO.
9. O backend classifica tecnicamente o defeito:
   - libera orientação de nota para casos conclusivos, como `não liga`, `queimado` ou `sem sinal de vida`;
   - pede testes para casos como reiniciando, TAG, tela/listras ou defeitos intermitentes;
   - libera caso o cliente já tenha enviado testes, vídeos, anexos ou informado que o defeito persistiu.
10. Se o item estiver liberado, o app baixa a DANFE em PDF pelo link do UNO e extrai número da nota, data, NCM e valor unitário.
11. O app calcula se está em garantia, mas fora de garantia também segue para RMA.
12. O app monta uma resposta automática:
    - template de orientação de nota para itens aptos;
    - pedido de correção quando faltam dados;
    - pedido de testes/evidências quando a triagem técnica exige.
13. Se houver várias RMAs no mesmo e-mail, o app processa cada item separadamente e envia uma única resposta consolidada.
14. Após responder, o app aplica a label `RMA PROCESSADO` e remove `UNREAD` da thread.

a IA não abre RMA e não toma decisão. Ela só transforma o e-mail em dados estruturados. As decisões ficam no backend em C#.


## 1. Apto Simples

**Assunto:** RMA_TESTE Apto simples

**Corpo do e-mail:**

Boa tarde!

Segue solicitação de RMA.

CNPJ: 36045173000173
NS: 0M0200/013D88
Defeito: Não liga mais

Obrigado.

**Resultado esperado:** envia o template HTML de orientação de nota para o serial `0M0200/013D88`.

## 2. Sem Defeito

**Assunto:** RMA_TESTE Sem defeito

**Corpo do e-mail:**

Boa tarde!

Segue solicitação de RMA.

CNPJ: 36045173000173
NS: 0M0200/013D88

Obrigado.

**Resultado esperado:** responde pedindo o campo faltante `defeito`.

## 3. Sem CNPJ

**Assunto:** RMA_TESTE Sem CNPJ

**Corpo do e-mail:**

Boa tarde!

Segue solicitação de RMA.

NS: 0M0200/013D88
Defeito: Não liga mais

Obrigado.

**Resultado esperado:** responde pedindo o campo faltante `cnpj`.

## 4. CNPJ Inválido

**Assunto:** RMA_TESTE CNPJ inválido

**Corpo do e-mail:**

Boa tarde!

Segue solicitação de RMA.

CNPJ: 12345678900000
NS: 0M0200/013D88
Defeito: Não liga mais

Obrigado.

**Resultado esperado:** responde pedindo `CNPJ válido`.

## 5. Precisa de Testes: Reiniciando

**Assunto:** RMA_TESTE Precisa de testes reiniciando

**Corpo do e-mail:**

Boa tarde!

Segue solicitação de RMA.

CNPJ: 36045173000173
NS: 0M0200/013D88
Produto: iDFace
Defeito: Fica reiniciando sozinho

Obrigado.

**Resultado esperado:** não envia template de nota. Responde pedindo testes/evidências, como testar fonte, testar isolado, verificar alimentação, tentar Recovery, realizar Factory Reset and Update Firmware e enviar vídeo da falha.

## 6. Precisa de Testes: TAG

**Assunto:** RMA_TESTE Precisa de testes TAG

**Corpo do e-mail:**

Boa tarde!

Segue solicitação de RMA.

CNPJ: 36045173000173
NS: 0X0200/004245
Produto: iDFace Pro Max
Defeito: Não lê nenhuma TAG

Obrigado.

**Resultado esperado:** não envia template de nota. Responde pedindo testes de TAG/cartão, compatibilidade, habilitação, atualização/restauração de firmware e vídeo da falha.

## 7. Libera por Evidência

**Assunto:** RMA_TESTE Libera por evidência

**Corpo do e-mail:**

Boa tarde!

Segue solicitação de RMA.

CNPJ: 36045173000173
NS: 0M0200/013D88
Produto: iDFace
Defeito: Fica reiniciando sozinho

Já realizamos os testes com outra fonte, factory reset e atualização de firmware, mas o defeito persistiu.
Segue vídeo em anexo.

Obrigado.

**Resultado esperado:** envia o template HTML de orientação de nota, porque o e-mail informa testes realizados, atualização/reset, defeito persistente e anexo.

## 8. Uma Apta e Uma com Testes

**Assunto:** RMA_TESTE Uma apta e uma com testes

**Corpo do e-mail:**

Boa tarde!

Segue relação para abertura de RMA.

CNPJ: 36045173000173

SÉRIE: 0M0200/013D88
DEFEITO: Não liga mais
PRODUTO: iDFace

SÉRIE: 0X0200/004245
DEFEITO: Apresenta listras brancas na tela
PRODUTO: iDFace Pro Max

Obrigado.

**Resultado esperado:** envia template de nota para `0M0200/013D88` e inclui seção final pedindo testes/evidências para `0X0200/004245`.

## 9. Ambas Aptas

**Assunto:** RMA_TESTE Ambas aptas

**Corpo do e-mail:**

Boa tarde!

Segue relação para abertura de RMA.

CNPJ: 36045173000173

SÉRIE: 0M0200/013D88
DEFEITO: Não liga mais
PRODUTO: iDFace

SÉRIE: 0X0200/004245
DEFEITO: Sem sinal de vida
PRODUTO: iDFace Pro Max

Obrigado.

**Resultado esperado:** envia o template HTML de orientação de nota com dois blocos separados e coloridos:

- RMA 1 - Série `0M0200/013D88`;
- RMA 2 - Série `0X0200/004245`.

Cada bloco deve aparecer separado por `------------------------------------`.

## 10. Ambas Precisam de Testes

**Assunto:** RMA_TESTE Ambas precisam de testes

**Corpo do e-mail:**

Boa tarde!

Segue relação para abertura de RMA.

CNPJ: 36045173000173

SÉRIE: 0M0200/013D88
DEFEITO: Fica reiniciando
PRODUTO: iDFace

SÉRIE: 0X0200/004245
DEFEITO: Não lê TAG
PRODUTO: iDFace Pro Max

Obrigado.

**Resultado esperado:** não envia template de nota. Envia resposta textual consolidada pedindo testes/evidências para os dois seriais.

## 11. Serial Não Encontrado

**Assunto:** RMA_TESTE Serial não encontrado

**Corpo do e-mail:**

Boa tarde!

Segue solicitação de RMA.

CNPJ: 36045173000173
NS: 0A0A00/00AAAA
Defeito: Não liga mais

Obrigado.

**Resultado esperado:** se o serial `0A0A00/00AAAA` não existir no UNO, responde que não encontrou o equipamento e pede para verificar o número de série.

## 12. Falso Positivo EQUIPAMENTO

**Assunto:** RMA_TESTE Falso positivo EQUIPAMENTO

**Corpo do e-mail:**

Boa tarde!

Segue solicitação de RMA.

CNPJ: 36045173000173
N° SERIE EQUIPAMENTO:
Defeito: Não liga

Obrigado.

**Resultado esperado:** não aceita `EQUIPAMENTO` como serial. Responde pedindo o campo faltante `serial`.

## 13. Serial Antigo

**Assunto:** RMA_TESTE Serial antigo

**Corpo do e-mail:**

Boa tarde!

Segue solicitação de RMA.

CNPJ: 36045173000173
NS: L249
Defeito: Não liga

Obrigado.

**Resultado esperado:** aceita `L249` como formato legado. Se existir no UNO, segue o fluxo normal e envia orientação de nota.

## 14. Tabela Revenda

**Assunto:** RMA_TESTE Tabela revenda

**Corpo do e-mail:**

Boa tarde! Tudo bem?

Segue abaixo relação de produtos para abertura de RMA.

Aguardo envio de dados para emissão de nota.

OBSERVAÇÕES
SÉRIE
DEFEITO
PRODUTO

GARANTIA
0M0200/013D88
não liga, está queimada
LEITOR FACIAL IP 1080P IP65 COM SENHA E TAG 125KHZ - IDFACE PRO

GARANTIA
0X0200/004245
Listras brancas quando equipamento está ligado
LEITOR FACIAL IP 1080P IP65 COM SENHA E TAG 125KHZ - IDFACE PRO MAX

CNPJ: 36045173000173

Atenciosamente.

**Resultado esperado:** identifica duas RMAs. `0M0200/013D88` fica apto porque o defeito é `não liga/queimada`. `0X0200/004245` precisa de testes porque o defeito cita listras/tela. A resposta deve ter template de nota para o primeiro e seção de testes para o segundo.

## 15. Evidências Enviadas Depois

**Assunto:** RMA_TESTE Evidências enviadas depois

**Corpo do e-mail:**

Boa tarde! Tudo bem?

Em anexo, seguem vídeos dos produtos após atualização de firmware.
Os testes foram realizados e os defeitos persistiram.

CNPJ: 36045173000173

SÉRIE: 0M0200/013D88
DEFEITO: Fica reiniciando
PRODUTO: iDFace

SÉRIE: 0X0200/004245
DEFEITO: Apresenta listras brancas na tela
PRODUTO: iDFace Pro Max

Obrigado.

**Resultado esperado:** envia o template HTML de orientação de nota para os dois seriais, porque o e-mail informa anexos, vídeos, atualização de firmware, testes realizados e defeitos persistentes.

## Critérios Gerais Para Validar

Durante a apresentação, conferir:

- o e-mail recebe a label `RMA PROCESSADO`;
- a thread fica sem `UNREAD`;
- o sistema não responde em cima da própria resposta;
- múltiplas RMAs aparecem em uma única resposta;
- itens aptos e itens com testes ficam separados corretamente;
- seriais inválidos ou ausentes não são consultados como se fossem válidos;
- o template apto mantém as cores e destaques do modelo;
- quando há mais de uma RMA apta, os blocos aparecem separados e coloridos.
