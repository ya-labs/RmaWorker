# Interface Chatbot RMA

## Objetivo

Criar uma interface simples para o tecnico colar o e-mail recebido do cliente e receber um template de resposta pronto.

Nesta fase, a interface nao envia e-mail automaticamente e nao abre OS/RMA. O objetivo e reduzir tempo na resposta manual e demonstrar a otimizacao para a equipe.

## Regras Ajustadas Apos Apresentacao

- Testes passam a ser obrigatorios para seguir com orientacao de RMA.
- `Nao liga` e considerado vago quando nao ha confirmacao de teste com outra fonte ou evidencia equivalente.
- Se o cliente nao informou testes, a resposta deve pedir testes antes de seguir.
- Se o defeito for muito generico, a resposta deve pedir mais detalhes e nao orientar abertura.

## Arquitetura Atual

O frontend depende do backend para funcionar.

Frontend:

```text
RmaChatbot
```

Backend/API:

```text
RmaWorker
```

Endpoint principal:

```text
POST http://localhost:5000/api/rma/analyze
```

Health check:

```text
GET http://localhost:5000/api/health
```

## Comportamento Atual

A tela permite:

- colar o corpo do e-mail recebido;
- enviar o texto para o backend;
- usar o Ollama para extrair numero de serie, CNPJ, produto e defeito;
- validar CNPJ no backend;
- consultar o serial no UNO;
- aplicar as regras tecnicas do `RmaTechnicalClassifier`;
- extrair dados da nota quando o item estiver apto;
- receber uma resposta sugerida;
- copiar o texto ou HTML sugerido.

O frontend nao aplica regra de negocio localmente. A resposta depende da API do `RmaWorker`.

## Como Rodar

Backend:

```powershell
dotnet run --project RmaWorker
```

Frontend:

```powershell
cd RmaChatbot
npm install
npm run dev
```

URLs locais:

```text
http://localhost:5000/api/health
http://localhost:5173
```

Dependencias:

- Ollama rodando em `http://localhost:11434`;
- modelo configurado em `RmaWorker/appsettings.json`;
- acesso ao UNO para consulta de serial.

## Observacao Sobre o Worker do Gmail

O worker automatico do Gmail fica desabilitado por padrao nesta fase:

```json
"EnableEmailWorker": false
```

Assim, a demonstracao usa apenas a API manual e nao responde e-mails automaticamente.
