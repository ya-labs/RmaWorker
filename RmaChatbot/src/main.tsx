import React from 'react';
import ReactDOM from 'react-dom/client';
import { CheckCircle2, Clipboard, FileText, RotateCcw, Send, Server, ShieldAlert } from 'lucide-react';
import './styles.css';

type ApiResult = {
  extraction: {
    serial?: string | null;
    cnpj?: string | null;
    defeito?: string | null;
    produto?: string | null;
  };
  status: string;
  reason?: string | null;
  missingFields: string[];
};

type ApiResponse = {
  status: string;
  isHtml: boolean;
  responseBody: string;
  results: ApiResult[];
};

const initialEmail = `Boa tarde!

Segue solicitação de RMA.

CNPJ: 36045173000173
NS: 0M0200/013D88
Produto: iDFace
Defeito: Não liga

Obrigado.`;

const emptyResponse: ApiResponse = {
  status: 'AGUARDANDO',
  isHtml: false,
  responseBody: 'Cole o e-mail recebido e gere a resposta pelo backend.',
  results: [],
};

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL?.replace(/\/$/, '') ?? '';

function statusLabel(status: string) {
  const labels: Record<string, string> = {
    AGUARDANDO: 'Aguardando',
    APTO: 'Template pronto',
    PRECISA_TESTES: 'Pedir testes',
    PRECISA_DETALHES: 'Pedir detalhes',
    DADOS_AUSENTES: 'Dados faltantes',
    CNPJ_INVALIDO: 'CNPJ inválido',
    SERIAL_NAO_ENCONTRADO: 'Serial não encontrado',
    PENDENTE: 'Pendente',
    ERRO: 'Erro',
  };

  return labels[status] ?? status;
}

function firstExtraction(response: ApiResponse) {
  return response.results[0]?.extraction;
}

function plainTextFromHtml(html: string) {
  const element = document.createElement('div');
  element.innerHTML = html;
  return element.innerText;
}

function App() {
  const [email, setEmail] = React.useState(initialEmail);
  const [response, setResponse] = React.useState<ApiResponse>(emptyResponse);
  const [copied, setCopied] = React.useState(false);
  const [loading, setLoading] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  const extraction = firstExtraction(response);
  const isReady = response.status === 'APTO';

  async function handleAnalyze() {
    setLoading(true);
    setCopied(false);
    setError(null);

    try {
      const apiResponse = await fetch(`${apiBaseUrl}/api/rma/analyze`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          emailBody: email,
          subject: 'Analise manual RMA',
        }),
      });

      if (!apiResponse.ok) {
        const body = await apiResponse.text();
        throw new Error(body || `Erro HTTP ${apiResponse.status}`);
      }

      setResponse(await apiResponse.json());
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Falha ao consultar o backend.';
      setError(message);
      setResponse({
        ...emptyResponse,
        status: 'ERRO',
        responseBody: 'Não foi possível gerar a resposta. Verifique se o backend está rodando em http://localhost:5000, se o Vite foi reiniciado após a configuração do proxy e se o Ollama está disponível.',
      });
    } finally {
      setLoading(false);
    }
  }

  async function handleCopy() {
    if (response.isHtml && 'ClipboardItem' in window) {
      const html = new Blob([response.responseBody], { type: 'text/html' });
      const text = new Blob([plainTextFromHtml(response.responseBody)], { type: 'text/plain' });
      await navigator.clipboard.write([new ClipboardItem({ 'text/html': html, 'text/plain': text })]);
    } else {
      await navigator.clipboard.writeText(
        response.isHtml ? plainTextFromHtml(response.responseBody) : response.responseBody,
      );
    }

    setCopied(true);
  }

  function handleReset() {
    setEmail('');
    setResponse(emptyResponse);
    setError(null);
    setCopied(false);
  }

  return (
    <main className="app-shell">
      <section className="workspace" aria-label="Assistente RMA">
        <header className="topbar">
          <div>
            <p className="eyebrow">Triagem assistida com backend</p>
            <h1>Assistente RMA</h1>
          </div>
          <div className={`status-pill ${isReady ? 'status-ready' : 'status-warning'}`}>
            {isReady ? <CheckCircle2 size={18} /> : <ShieldAlert size={18} />}
            <span>{statusLabel(response.status)}</span>
          </div>
        </header>

        <div className="backend-strip">
          <Server size={18} />
          <span>Backend esperado em http://localhost:5000 usando Ollama, UNO e regras do RmaWorker.</span>
        </div>

        <div className="chat-layout">
          <section className="composer" aria-label="Entrada do e-mail">
            <div className="message incoming">
              <div className="message-header">
                <FileText size={18} />
                <span>E-mail recebido</span>
              </div>
              <textarea
                value={email}
                onChange={(event) => setEmail(event.target.value)}
                spellCheck="false"
                aria-label="Cole aqui o e-mail recebido"
              />
            </div>
            <div className="actions">
              <button className="secondary-button" type="button" onClick={handleReset} title="Limpar">
                <RotateCcw size={18} />
                <span>Limpar</span>
              </button>
              <button className="primary-button" type="button" onClick={handleAnalyze} disabled={loading} title="Gerar resposta">
                <Send size={18} />
                <span>{loading ? 'Consultando backend' : 'Gerar resposta'}</span>
              </button>
            </div>
          </section>

          <section className="result" aria-label="Resposta sugerida">
            <div className="message outgoing">
              <div className="message-header">
                <Clipboard size={18} />
                <span>Resposta sugerida</span>
              </div>
              <div className="extracted-grid">
                <div>
                  <span>Série</span>
                  <strong>{extraction?.serial || 'Não identificado'}</strong>
                </div>
                <div>
                  <span>CNPJ</span>
                  <strong>{extraction?.cnpj || 'Não identificado'}</strong>
                </div>
                <div>
                  <span>Defeito</span>
                  <strong>{extraction?.defeito || 'Não identificado'}</strong>
                </div>
              </div>
              {error ? <div className="error-box">{error}</div> : null}
              {response.isHtml ? (
                <div className="html-preview" dangerouslySetInnerHTML={{ __html: response.responseBody }} />
              ) : (
                <pre>{response.responseBody}</pre>
              )}
              <button className="copy-button" type="button" onClick={handleCopy} title="Copiar resposta">
                <Clipboard size={18} />
                <span>{copied ? 'Copiado' : 'Copiar resposta'}</span>
              </button>
            </div>
          </section>
        </div>
      </section>
    </main>
  );
}

ReactDOM.createRoot(document.getElementById('root')!).render(<App />);
