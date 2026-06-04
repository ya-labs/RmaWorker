import React from 'react';
import ReactDOM from 'react-dom/client';
import { CheckCircle2, Clipboard, FileText, Hash, Mail, RotateCcw, Send, ShieldAlert } from 'lucide-react';
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

type HealthStatus = 'checking' | 'online' | 'offline';
type RequestMode = 'email' | 'serial';

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
  const [requestMode, setRequestMode] = React.useState<RequestMode>('email');
  const [email, setEmail] = React.useState(initialEmail);
  const [serial, setSerial] = React.useState('');
  const [response, setResponse] = React.useState<ApiResponse>(emptyResponse);
  const [copied, setCopied] = React.useState(false);
  const [loading, setLoading] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);
  const [healthStatus, setHealthStatus] = React.useState<HealthStatus>('checking');

  const extraction = firstExtraction(response);
  const isReady = response.status === 'APTO';
  const canSubmit = requestMode === 'email' ? email.trim().length > 0 : serial.trim().length > 0;

  React.useEffect(() => {
    let active = true;

    async function checkHealth() {
      try {
        const healthResponse = await fetch(`${apiBaseUrl}/api/health`, {
          cache: 'no-store',
        });

        if (active) {
          setHealthStatus(healthResponse.ok ? 'online' : 'offline');
        }
      } catch {
        if (active) {
          setHealthStatus('offline');
        }
      }
    }

    setHealthStatus('checking');
    void checkHealth();
    const intervalId = window.setInterval(checkHealth, 15000);

    return () => {
      active = false;
      window.clearInterval(intervalId);
    };
  }, []);

  async function handleGenerate() {
    setLoading(true);
    setCopied(false);
    setError(null);

    try {
      const endpoint = requestMode === 'email'
        ? '/api/rma/analyze'
        : '/api/rma/generate-by-serial';
      const requestBody = requestMode === 'email'
        ? {
          emailBody: email,
          subject: 'Analise manual RMA',
        }
        : {
          serial,
        };

      const apiResponse = await fetch(`${apiBaseUrl}${endpoint}`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(requestBody),
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
        responseBody: 'Não foi possível gerar a resposta. Verifique se a API pública está acessível, se a URL configurada no GitHub Pages usa HTTPS e se o Ollama está disponível.',
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
    setSerial('');
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

        <div className={`api-health health-${healthStatus}`}>
          <span className="health-dot" aria-hidden="true" />
          <span>
            API {healthStatus === 'checking' ? 'verificando' : healthStatus === 'online' ? 'online' : 'offline'}
          </span>
        </div>

        <div className="chat-layout">
          <section className="composer" aria-label="Entrada do e-mail">
            <div className="mode-switch" role="tablist" aria-label="Modo de geracao">
              <button
                className={requestMode === 'email' ? 'mode-button active' : 'mode-button'}
                type="button"
                onClick={() => setRequestMode('email')}
                role="tab"
                aria-selected={requestMode === 'email'}
                title="Analisar e-mail recebido"
              >
                <Mail size={18} />
                <span>E-mail recebido</span>
              </button>
              <button
                className={requestMode === 'serial' ? 'mode-button active' : 'mode-button'}
                type="button"
                onClick={() => setRequestMode('serial')}
                role="tab"
                aria-selected={requestMode === 'serial'}
                title="Gerar e-mail pelo numero de serie"
              >
                <Hash size={18} />
                <span>Somente serie</span>
              </button>
            </div>
            <div className="message incoming">
              <div className="message-header">
                {requestMode === 'email' ? <FileText size={18} /> : <Hash size={18} />}
                <span>{requestMode === 'email' ? 'E-mail recebido' : 'Numero de serie'}</span>
              </div>
              {requestMode === 'email' ? (
                <textarea
                  value={email}
                  onChange={(event) => setEmail(event.target.value)}
                  spellCheck="false"
                  aria-label="Cole aqui o e-mail recebido"
                />
              ) : (
                <div className="serial-panel">
                  <label htmlFor="serial-input">Serie do equipamento</label>
                  <input
                    id="serial-input"
                    value={serial}
                    onChange={(event) => setSerial(event.target.value)}
                    placeholder="0M0200/013D88"
                    spellCheck="false"
                    aria-label="Informe o numero de serie"
                  />
                </div>
              )}
            </div>
            <div className="actions">
              <button className="secondary-button" type="button" onClick={handleReset} title="Limpar">
                <RotateCcw size={18} />
                <span>Limpar</span>
              </button>
              <button className="primary-button" type="button" onClick={handleGenerate} disabled={loading || !canSubmit} title="Gerar resposta">
                <Send size={18} />
                <span>{loading ? 'Consultando backend' : requestMode === 'email' ? 'Gerar resposta' : 'Gerar por serie'}</span>
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
