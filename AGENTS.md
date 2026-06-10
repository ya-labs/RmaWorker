# AGENTS.md

## Objetivo

Este documento define as diretrizes para agentes de IA e desenvolvedores que contribuem com este projeto.

O objetivo e manter consistencia de codigo, regras de negocio, seguranca e qualidade das entregas.

## Tecnologias

- .NET
- ASP.NET Core
- C#
- React
- Vite
- TypeScript
- Azure
- GitHub Actions
- REST APIs
- Playwright

## Contexto do Projeto

O RMIA automatiza parte do processo de RMA e abertura de O.S no UNO.

- Backend principal: `RmaWorker`, em .NET 8.
- Frontend principal: `RmaChatbot`, em React/Vite.
- O fluxo principal atual e a interface manual para manutencao e envio de pecas.
- O UNO e acessado via Playwright para abertura de O.S em sistema legado.
- A consulta de serial usa o endpoint interno do UNO configurado em `SerialValidation__BaseUrl`.
- A automacao do Gmail existe, mas nao deve ser priorizada no fluxo operacional atual.

## Comunicacao

- Responder em pt-BR.
- Ser direto e objetivo.
- Evitar emojis.
- Quando pedir texto de PR, usar: Contexto, O que mudou, Validacao, Observacoes.
- Quando pedir issue, usar: Descricao, Escopo, Criterios de aceite, Dependencias.

## Fluxo de Desenvolvimento

### Branches

Utilizar o padrao:

```text
feat/nome-da-feature
fix/nome-do-ajuste
refactor/nome-da-refatoracao
```

Exemplos:

```text
feat/validar-cnpj-uno
fix/cnpj-retornando-incorreto
feat/template-envio-peca
```

### Commits

Utilizar Conventional Commits.

Exemplos:

```text
feat: validar cnpj antes de gerar template
fix: corrigir retorno de cnpj na tela
refactor: simplificar fluxo de abertura de os
docs: atualizar documentacao da api
```

### Pull Requests

Toda alteracao deve possuir uma Issue relacionada.

O PR deve:

- Descrever claramente o problema.
- Informar a solucao implementada.
- Relacionar a Issue.
- Adicionar evidencias quando aplicavel.
- Informar validacoes realizadas.

Exemplo:

```text
Closes #12
```

## Regras de Negocio

### Validacao de CNPJ

Antes de gerar qualquer template:

1. Consultar o CNPJ no UNO.
2. Validar existencia do cliente.
3. Caso nao exista:
   - nao gerar template;
   - nao abrir O.S;
   - retornar erro apropriado.

### Manutencao

- O fluxo de manutencao deve validar CNPJ no UNO antes de gerar template.
- O template de manutencao pode ser gerado antes da abertura da O.S.
- O checkbox de manutencao em garantia altera apenas a categoria da O.S.
- Manutencao deve manter `codStatus` como `10 - Aguardando NF`.

### Envio de Pecas

Codigo de operacao:

```text
7 - Remessa de pecas
```

Campos obrigatorios:

- Peca a ser enviada
- Numero de serie
- Defeito
- CNPJ da revenda

Mapeamento:

```text
Peca a ser enviada -> observacoes (UNO)
```

Fluxo obrigatorio:

```text
Receber solicitacao
|
v
Validar CNPJ no UNO
|
v
Abrir O.S no UNO
|
v
Obter numero da O.S
|
v
Gerar template de resposta
```

O template de envio de pecas depende do numero da O.S e nunca deve ser gerado antes da abertura da O.S.

Envio de pecas deve alterar `codStatus` no UNO para:

```text
15 - Aguardando envio
```

### Alertas do UNO

Se o UNO retornar alerta de regra de negocio, como serial ja usado em O.S:

- nao continuar a abertura da O.S daquele item;
- retornar a mensagem do UNO para a interface;
- nao gerar sucesso falso.

## Boas Praticas

### Servicos

- Um servico deve possuir apenas uma responsabilidade.
- Evitar regras de negocio em Controllers.
- Centralizar integracoes externas em Services especificos.
- Reutilizar codigo existente quando possivel.
- Nao criar abstracoes desnecessarias.

### Contratos

- Usar DTOs para contratos HTTP.
- Nao alterar contratos publicos sem justificativa.
- Manter regras de negocio no backend, nao no frontend.
- Quando alterar contrato entre frontend e backend, atualizar ambos no mesmo PR.

### Frontend

- Manter a interface como ferramenta operacional direta.
- Nao criar landing page ou tela explicativa sem necessidade.
- Usar icones do `lucide-react` quando houver botoes de acao.
- Evitar textos grandes explicando a funcionalidade dentro da tela.
- Garantir que o layout seja utilizavel em desktop e mobile.

## Tratamento de Erros

- Nunca utilizar `catch` vazio.
- Registrar erros relevantes.
- Retornar mensagens claras para falhas de negocio.
- Distinguir falha tecnica de falha de regra de negocio.
- Evitar mascarar erro do UNO como sucesso.

## Logging

Registrar:

- Consultas ao UNO
- Abertura de O.S
- Falhas de integracao
- Erros inesperados
- Tempo de etapas relevantes quando aplicavel

Nunca registrar:

- Senhas
- Tokens
- Credenciais
- Chaves privadas
- Dados sensiveis desnecessarios

## Seguranca

- Nunca versionar credenciais, tokens, senhas, certificados ou arquivos de autenticacao.
- Usar variaveis de ambiente, User Secrets ou GitHub Secrets para dados sensiveis.
- Nao repetir credenciais em respostas, commits, PRs ou issues.
- `docs/`, `temp/`, `artifacts/`, `credentials.json`, `token.json` e `node_modules/` devem permanecer fora do versionamento.

## Testes e Validacao

Antes de abrir um PR:

- Validar fluxo principal.
- Validar cenarios de erro.
- Garantir que nao houve regressao em funcionalidades existentes.
- Validar fluxo no UNO quando a alteracao tocar abertura de O.S.

Comandos recomendados:

```powershell
dotnet build .\RmaWorker\RmaWorker.csproj -p:UseAppHost=false -o .\artifacts\build-check\rmaworker
```

```powershell
cd RmaChatbot
npm run build
```

Se o backend estiver rodando localmente, o build normal pode falhar por arquivo bloqueado. Nesse caso, usar saida separada em `artifacts/build-check`.

## CI/CD

O deploy e realizado atraves do GitHub Actions.

Regras:

- Nao alterar workflows sem necessidade.
- Garantir que o build esteja passando localmente antes do PR.
- Toda alteracao deve manter compatibilidade com o pipeline existente.
- Frontend: workflow `Deploy RMA Chatbot Frontend`.
- Backend: workflow `Deploy RMA Backend`.
- A API publica deve responder em `/api/health` antes de considerar deploy operacional.

## O Que Um Agente de IA Deve Fazer

Ao implementar uma alteracao:

- Ler a Issue completa.
- Entender a regra de negocio antes de alterar codigo.
- Fazer a menor alteracao possivel.
- Reutilizar codigo existente quando possivel.
- Nao criar abstracoes desnecessarias.
- Nao alterar contratos publicos sem justificativa.
- Atualizar documentacao quando necessario.
- Validar build e fluxo afetado quando aplicavel.

## O Que Um Agente de IA Nao Deve Fazer

- Alterar regras de negocio sem solicitacao explicita.
- Criar dependencias novas sem justificativa.
- Alterar CI/CD sem necessidade.
- Remover logs existentes sem motivo.
- Ignorar validacoes de integracao com o UNO.
- Registrar ou expor credenciais.
