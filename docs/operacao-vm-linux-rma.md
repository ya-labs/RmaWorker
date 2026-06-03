# Operacao da VM Linux do RMA

## Visao Geral

Ambiente atual:

- Frontend publicado no GitHub Pages.
- Backend `RmaWorker` rodando em uma VM Linux na Azure.
- VM conectada na VPN via OpenVPN para acessar o UNO.
- Ollama rodando na propria VM em `http://localhost:11434`.
- Caddy fazendo HTTPS e proxy para o backend.

URLs:

```text
API local na VM: http://localhost:5000
API publica HTTPS: https://20-59-116-168.sslip.io
Health check: https://20-59-116-168.sslip.io/api/health
```

IP publico atual:

```text
20.59.116.168
```

Usuario SSH:

```text
azureuser
```

## Ligar a VM

No Azure Portal:

1. Acesse a VM `rworker-linux-01`.
2. Clique em `Start`.
3. Aguarde o status ficar `Running`.

Depois conecte por SSH no PowerShell:

```powershell
ssh -i "$env:USERPROFILE\Downloads\rworker-linux-01_key.pem" azureuser@20.59.116.168
```

Se o IP publico mudar, atualize o comando SSH e o dominio `sslip.io`.

## Desligar a VM

No Azure Portal:

1. Acesse a VM `rworker-linux-01`.
2. Clique em `Stop`.
3. Confirme.
4. Aguarde o status ficar:

```text
Stopped (deallocated)
```

Importante: desligar apenas pelo Linux com `shutdown` pode nao desalocar a VM. Para parar cobranca de computacao, prefira o botao `Stop` no Portal.

## Conectar a VPN OpenVPN

O arquivo `.ovpn` fica em:

```text
/home/azureuser/Controlid.ovpn
```

Para conectar:

```bash
sudo openvpn --config /home/azureuser/Controlid.ovpn
```

Informe usuario e senha quando pedir.

Quando aparecer:

```text
Initialization Sequence Completed
```

a VPN esta conectada.

Esse comando roda em primeiro plano. Mantenha esse terminal aberto enquanto precisar consultar o UNO.

Para testar o UNO em outro terminal SSH:

```bash
curl "http://uno.controlid.com.br/supplychain/consultar.sh?0M0200/013D88" | head
```

Se responder HTML/dados do UNO, a VPN esta funcionando.

## Ollama

Verificar status:

```bash
sudo systemctl status ollama
```

Iniciar:

```bash
sudo systemctl start ollama
```

Habilitar no boot:

```bash
sudo systemctl enable ollama
```

Listar modelos:

```bash
ollama list
```

Baixar modelo usado pelo backend:

```bash
ollama pull qwen3:4b
```

Testar API do Ollama:

```bash
curl http://localhost:11434/api/tags
```

## Rodar o Backend Manualmente

Pasta do backend publicado:

```text
/home/azureuser/rma-backend
```

Com a VPN conectada e o Ollama rodando:

```bash
cd /home/azureuser/rma-backend

ASPNETCORE_URLS=http://0.0.0.0:5000 \
Worker__EnableEmailWorker=false \
Ollama__BaseUrl=http://localhost:11434 \
Ollama__Model=qwen3:4b \
dotnet RmaWorker.dll
```

Health check local:

```bash
curl http://localhost:5000/api/health
```

Health check publico:

```bash
curl https://20-59-116-168.sslip.io/api/health
```

## Caddy e HTTPS

Arquivo de configuracao:

```text
/etc/caddy/Caddyfile
```

Conteudo esperado:

```text
20-59-116-168.sslip.io {
    reverse_proxy localhost:5000
}
```

Recarregar Caddy:

```bash
sudo systemctl reload caddy
```

Ver status:

```bash
sudo systemctl status caddy
```

Ver logs:

```bash
journalctl -u caddy --no-pager -n 80
```

## Portas Abertas na Azure

No Network Security Group da VM:

```text
22 TCP   - SSH
80 TCP   - HTTP para emissao/renovacao do certificado do Caddy
443 TCP  - HTTPS publico da API
5000 TCP - API direta, pode ser fechada depois que HTTPS estiver validado
```

Recomendacao: depois que `https://20-59-116-168.sslip.io/api/health` estiver funcionando, a porta `5000` publica pode ser removida do NSG. O Caddy acessa `localhost:5000` dentro da propria VM.

## Publicar Nova Versao do Backend

No Windows, dentro de `D:\projects\RMIA`:

```powershell
dotnet publish "RmaWorker\RmaWorker.csproj" -c Release -o ".\artifacts\rma-backend"
```

Copiar para a VM:

```powershell
scp -i "$env:USERPROFILE\Downloads\rworker-linux-01_key.pem" -r D:\projects\RMIA\artifacts\rma-backend azureuser@20.59.116.168:/home/azureuser/
```

Na VM, pare o backend antigo se ele estiver rodando no terminal com `Ctrl+C`, depois rode novamente:

```bash
cd /home/azureuser/rma-backend

ASPNETCORE_URLS=http://0.0.0.0:5000 \
Worker__EnableEmailWorker=false \
Ollama__BaseUrl=http://localhost:11434 \
Ollama__Model=qwen3:4b \
dotnet RmaWorker.dll
```

## Git e Branches

Criar branch para nova feature:

```powershell
git checkout -b feat/nome-da-feature
```

Commit:

```powershell
git add .
git commit -m "Implementa nova regra de triagem"
```

Push:

```powershell
git push -u origin feat/nome-da-feature
```

Depois abrir PR para `main`.

## GitHub Pages

Variavel usada no build do frontend:

```text
RMA_API_BASE_URL=https://20-59-116-168.sslip.io
```

Configurar em:

```text
GitHub > Settings > Secrets and variables > Actions > Variables
```

Depois rodar:

```text
Actions > Deploy RMA Chatbot Frontend > Run workflow
```

## Checklist Para Deixar Tudo Online

1. VM esta `Running`.
2. SSH conecta.
3. OpenVPN conectada com `Initialization Sequence Completed`.
4. UNO responde via `curl`.
5. Ollama esta ativo.
6. Modelo `qwen3:4b` esta baixado.
7. Backend esta rodando na porta `5000`.
8. Caddy esta ativo.
9. `https://20-59-116-168.sslip.io/api/health` retorna `{"status":"ok"}`.
10. GitHub Pages esta apontando para `https://20-59-116-168.sslip.io`.

## Problemas Comuns

### GitHub Pages mostra erro de fetch

Verifique:

```bash
curl https://20-59-116-168.sslip.io/api/health
```

Se nao responder, confira backend, Caddy e portas 80/443.

### Backend nao consulta UNO

Verifique se a VPN esta conectada:

```bash
ip addr show tun0
```

Teste:

```bash
curl "http://uno.controlid.com.br/supplychain/consultar.sh?0M0200/013D88" | head
```

### Backend falha no Ollama

Verifique:

```bash
sudo systemctl status ollama
curl http://localhost:11434/api/tags
ollama list
```

### HTTPS nao funciona

Verifique:

```bash
sudo systemctl status caddy
journalctl -u caddy --no-pager -n 80
```

Confirme se portas 80 e 443 estao liberadas no NSG da Azure.
