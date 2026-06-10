import React from 'react';
import ReactDOM from 'react-dom/client';
import { CheckCircle2, Clipboard, Hash, PackagePlus, RotateCcw, Send, ShieldAlert, Wrench } from 'lucide-react';
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

type ServiceOrderResponse = {
  status: string;
  message: string;
  items: Array<{
    serial: string;
    cnpj?: string | null;
    status: string;
    reason?: string | null;
    serviceOrderCode?: string | null;
  }>;
};

type HealthStatus = 'checking' | 'online' | 'offline';
type RequestMode = 'maintenance' | 'parts';

const emptyResponse: ApiResponse = {
  status: 'AGUARDANDO',
  isHtml: false,
  responseBody: 'Preencha os dados para consultar o UNO e gerar a resposta.',
  results: [],
};

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL?.replace(/\/$/, '') ?? '';

function statusLabel(status: string) {
  const labels: Record<string, string> = {
    AGUARDANDO: 'Aguardando',
    APTO: 'Template pronto',
    OS_ABERTA: 'O.S aberta',
    OS_PARCIAL: 'O.S parcial',
    CLIENTE_NAO_ENCONTRADO: 'CNPJ nao encontrado',
    UNO_CONFIG_INCOMPLETA: 'Configurar UNO',
    UNO_ERRO: 'Erro no UNO',
    PRECISA_TESTES: 'Pedir testes',
    PRECISA_DETALHES: 'Pedir detalhes',
    DADOS_AUSENTES: 'Dados faltantes',
    CNPJ_INVALIDO: 'CNPJ invalido',
    SERIAL_NAO_ENCONTRADO: 'Serial nao encontrado',
    PENDENTE: 'Pendente',
    ERRO: 'Erro',
  };

  return labels[status] ?? status;
}

function firstExtraction(response: ApiResponse) {
  return response.results[0]?.extraction;
}

function serialSummary(response: ApiResponse) {
  const serials = response.results
    .map((result) => result.extraction.serial)
    .filter((serial): serial is string => Boolean(serial));

  if (serials.length === 0) {
    return 'Nao identificado';
  }

  if (serials.length === 1) {
    return serials[0];
  }

  return `${serials.length} seriais`;
}

function splitSerials(value: string) {
  return value
    .split(/[\n,;]+/)
    .map((item) => item.trim())
    .filter(Boolean);
}

function plainTextFromHtml(html: string) {
  const element = document.createElement('div');
  element.innerHTML = html;
  return element.innerText;
}

function buildPartsTemplate(serviceOrderResponse: ServiceOrderResponse) {
  const openedItems = serviceOrderResponse.items.filter((item) => item.serviceOrderCode);
  const failedItems = serviceOrderResponse.items.filter((item) => item.status !== 'OS_ABERTA');
  const firstServiceOrder = openedItems[0]?.serviceOrderCode ?? 'preencher numero da OS';
  const failedHtml = failedItems.length === 0
    ? ''
    : `
      <br>
      <div style="color: #c00000; font-weight: 700;">Pendencias:</div>
      <ul>
        ${failedItems.map((item) => `<li>Serie ${html(item.serial)}: ${html(item.reason || item.status)}</li>`).join('')}
      </ul>
    `;

  return `
    <div style="font-family: Arial, Helvetica, sans-serif; font-size: 12px; line-height: 1.25; color: #000;">
      <strong>Sua solicitacao de peca foi registrada na OS ${html(firstServiceOrder)}</strong><br>
      <br>
      <span style="color: #ff0000; font-weight: 700;">Atencao : A peca danificada deve ser devolvida para nossa fabrica, destacando o numero da OS na embalagem juntamente com a NF de Simples Remessa.</span><br>
      <span style="color: #ff0000; font-weight: 700;">Segue dados abaixo para devolucao.</span><br><br>

      <span style="color: #ff0000; font-weight: 700;">OBS: O ENVIO E POR CONTA DO REMETENTE.</span><br><br>

      <em>( A nao devolucao desta peca pode impossibilitar novos envios )</em><br><br>

      <span style="background-color: #ffff00; font-weight: 700;">1) Iremos enviar uma peca em bonificacao;</span><br>
      <span style="background-color: #ffff00; font-weight: 700;">2) Juntamente com a peca sera enviada a NF de Bonificacao;</span><br>
      <span style="background-color: #ffff00; font-weight: 700;">3) Esta NF de bonificacao deve ser usada para preencher a NF de Simples Remessa para a devida devolucao da peca com defeito.</span><br><br>

      <table style="border-collapse: collapse; width: 704px; max-width: 100%; table-layout: fixed;">
        <tbody>
          <tr>
            <td style="width: 140px; border: 1px solid #000; padding: 2px; font-weight: 700;">Natureza da operacao :</td>
            <td style="border: 1px solid #000; padding: 2px;">Simples Remessa</td>
          </tr>
          <tr>
            <td style="border: 1px solid #000; padding: 2px;"></td>
            <td style="border: 1px solid #000; padding: 2px;">(Caso nao localize essa informacao, deixe em OUTRAS SAIDAS, mas e obrigatorio colocar nos dados adicionais SIMPLES REMESSA).</td>
          </tr>
          <tr>
            <td colspan="2" style="border: 1px solid #000; padding: 6px 2px; color: #ff0000; font-weight: 700;">DESTINATARIO:</td>
          </tr>
          <tr>
            <td style="border: 1px solid #000; padding: 2px; font-weight: 700;">Razao Social :</td>
            <td style="border: 1px solid #000; padding: 2px;">CONTROL ID IND. COM. DE HARDWARE E SERV. DE TECNOLOGIA LTDA</td>
          </tr>
          <tr>
            <td style="border: 1px solid #000; padding: 2px; font-weight: 700;">CNPJ :</td>
            <td style="border: 1px solid #000; padding: 2px;">08.238.299/0003-90</td>
          </tr>
          <tr>
            <td style="border: 1px solid #000; padding: 2px; font-weight: 700;">Inscricao Estadual</td>
            <td style="border: 1px solid #000; padding: 2px;">002531372.00-90</td>
          </tr>
          <tr>
            <td style="border: 1px solid #000; padding: 2px; font-weight: 700;">Endereco:</td>
            <td style="border: 1px solid #000; padding: 2px;">RUA JOSEPHA GOMES DE SOUZA , 298 - GALPAO 02 e 03</td>
          </tr>
          <tr>
            <td style="border: 1px solid #000; padding: 2px; font-weight: 700;">BAIRRO :</td>
            <td style="border: 1px solid #000; padding: 2px;">Dist. indust. Pires II</td>
          </tr>
          <tr>
            <td style="border: 1px solid #000; padding: 2px; font-weight: 700;">Cep :</td>
            <td style="border: 1px solid #000; padding: 2px;">37642-900</td>
          </tr>
          <tr>
            <td style="border: 1px solid #000; padding: 2px; font-weight: 700;">Municipio :</td>
            <td style="border: 1px solid #000; padding: 2px;">Extrema - MG.</td>
          </tr>
          <tr>
            <td style="border: 1px solid #000; padding: 2px; font-weight: 700;">Telefone Control:</td>
            <td style="border: 1px solid #000; padding: 2px;">(11) 3059-9900</td>
          </tr>
          <tr>
            <td colspan="2" style="border: 1px solid #000; padding: 8px 2px;"></td>
          </tr>
          <tr>
            <td colspan="2" style="border: 1px solid #000; padding: 6px 2px; color: #ff0000; font-weight: 700;">DADOS DO PRODUTO/SERVICO:</td>
          </tr>
          <tr>
            <td colspan="2" style="border: 1px solid #000; padding: 8px 2px;"></td>
          </tr>
          <tr>
            <td style="border: 1px solid #000; padding: 2px; font-weight: 700;">Descricao do Produto:</td>
            <td style="border: 1px solid #000; padding: 2px;"><span style="background-color: #ffff00; font-weight: 700;">Dados constantes na NF de Bonificacao enviada com as pecas novas.</span></td>
          </tr>
          <tr>
            <td style="border: 1px solid #000; padding: 2px; font-weight: 700;">NCM:</td>
            <td style="border: 1px solid #000; padding: 2px;"><span style="background-color: #ffff00; font-weight: 700;">Dados constantes na NF de Bonificacao enviada com as pecas novas.</span></td>
          </tr>
          <tr>
            <td style="border: 1px solid #000; padding: 2px; font-weight: 700;">CFOP:</td>
            <td style="border: 1px solid #000; padding: 2px;">5.949 - para Empresas dentro do Estado de Minas Gerais/6.949 - para Empresas fora do Estado de Minas Gerais</td>
          </tr>
          <tr>
            <td style="border: 1px solid #000; padding: 2px; font-weight: 700;">VALOR UNITARIO:</td>
            <td style="border: 1px solid #000; padding: 2px;"><span style="background-color: #ffff00; font-weight: 700;">Dados constantes na NF de Bonificacao enviada com as pecas novas.</span></td>
          </tr>
          <tr>
            <td style="border: 1px solid #000; padding: 2px; font-weight: 700;">ICMS</td>
            <td style="border: 1px solid #000; padding: 2px;">NAO MENCIONAR OS IMPOSTOS</td>
          </tr>
          <tr>
            <td style="border: 1px solid #000; padding: 2px; font-weight: 700;">IPI</td>
            <td style="border: 1px solid #000; padding: 2px;">NAO MENCIONAR OS IMPOSTOS</td>
          </tr>
          <tr>
            <td colspan="2" style="border: 1px solid #000; padding: 8px 2px;"></td>
          </tr>
          <tr>
            <td colspan="2" style="border: 1px solid #000; padding: 6px 2px; color: #ff0000; font-weight: 700;">Informacoes que devem constar no campo Dados Adicionais:</td>
          </tr>
          <tr>
            <td colspan="2" style="border: 1px solid #000; padding: 8px 2px;"></td>
          </tr>
          <tr>
            <td colspan="2" style="border: 1px solid #000; padding: 6px 2px;">Envio de pecas na garantia para o equipamento - <span style="color: #ff0000; font-weight: 700;">MENCIONAR O NUMERO DA OS.</span></td>
          </tr>
        </tbody>
      </table>
      ${failedHtml}
    </div>
  `;
}

function html(value: string) {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#039;');
}

function App() {
  const [requestMode, setRequestMode] = React.useState<RequestMode>('maintenance');
  const [serial, setSerial] = React.useState('');
  const [cnpj, setCnpj] = React.useState('');
  const [serviceOrderDefect, setServiceOrderDefect] = React.useState('');
  const [maintenanceInWarranty, setMaintenanceInWarranty] = React.useState(false);
  const [partToSend, setPartToSend] = React.useState('');
  const [response, setResponse] = React.useState<ApiResponse>(emptyResponse);
  const [copied, setCopied] = React.useState(false);
  const [loading, setLoading] = React.useState(false);
  const [openingServiceOrder, setOpeningServiceOrder] = React.useState(false);
  const [serviceOrderStatus, setServiceOrderStatus] = React.useState<string | null>(null);
  const [error, setError] = React.useState<string | null>(null);
  const [healthStatus, setHealthStatus] = React.useState<HealthStatus>('checking');

  const extraction = response.results.length > 1
    ? { ...firstExtraction(response), serial: serialSummary(response) }
    : firstExtraction(response);
  const isReady = response.status === 'APTO' || response.status === 'OS_ABERTA';
  const serials = splitSerials(serial);
  const canSubmit = serials.length > 0
    && cnpj.trim().length > 0
    && serviceOrderDefect.trim().length > 0
    && (requestMode === 'maintenance' || partToSend.trim().length > 0);
  const eligibleResults = response.results.filter((result) => result.status === 'APTO' && result.extraction.serial);
  const canOpenServiceOrder = requestMode === 'maintenance' && eligibleResults.length > 0;

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
    setServiceOrderStatus(null);

    try {
      if (requestMode === 'maintenance') {
        const apiResponse = await fetch(`${apiBaseUrl}/api/rma/generate-by-serial`, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
          },
          body: JSON.stringify({
            serials,
            cnpj,
            defectReported: serviceOrderDefect,
            maintenanceInWarranty,
          }),
        });

        if (!apiResponse.ok) {
          const body = await apiResponse.text();
          throw new Error(body || `Erro HTTP ${apiResponse.status}`);
        }

        setResponse(await apiResponse.json());
        return;
      }

      const serviceOrderResponse = await openServiceOrder('parts');
      setServiceOrderStatus(serviceOrderResponse.message);
      setResponse({
        status: serviceOrderResponse.status,
        isHtml: true,
        responseBody: buildPartsTemplate(serviceOrderResponse),
        results: serviceOrderResponse.items.map((item) => ({
          extraction: {
            serial: item.serial,
            cnpj: item.cnpj || cnpj,
            defeito: serviceOrderDefect,
          },
          status: item.status,
          reason: item.reason,
          missingFields: [],
        })),
      });
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Falha ao consultar o backend.';
      setError(message);
      setResponse({
        ...emptyResponse,
        status: 'ERRO',
        responseBody: 'Nao foi possivel concluir a operacao. Verifique a API, o acesso ao UNO e as configuracoes do backend.',
      });
    } finally {
      setLoading(false);
    }
  }

  async function openServiceOrder(type: RequestMode) {
    const apiResponse = await fetch(`${apiBaseUrl}/api/rma/service-order/open`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        cnpj,
        requestType: type,
        maintenanceInWarranty,
        partToSend: type === 'parts' ? partToSend : null,
        unoObservations: null,
        items: serials.map((item) => ({
          serial: item,
          defectReported: serviceOrderDefect,
          unoObservations: null,
        })),
      }),
    });

    if (!apiResponse.ok) {
      const body = await apiResponse.text();
      throw new Error(body || `Erro HTTP ${apiResponse.status}`);
    }

    return await apiResponse.json() as ServiceOrderResponse;
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

  async function handleOpenServiceOrder() {
    setOpeningServiceOrder(true);
    setServiceOrderStatus(null);
    setError(null);

    try {
      const serviceOrderResponse = await openServiceOrder('maintenance');
      const openedCodes = serviceOrderResponse.items
        .filter((item) => item.serviceOrderCode)
        .map((item) => `${item.serial}: O.S ${item.serviceOrderCode}`);
      const failedItems = serviceOrderResponse.items
        .filter((item) => item.status !== 'OS_ABERTA')
        .map((item) => `${item.serial}: ${item.reason || item.status}`);
      setServiceOrderStatus([
        serviceOrderResponse.message,
        ...openedCodes,
        ...failedItems,
      ].join('\n'));
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Falha ao abrir a O.S no sistema interno.';
      setError(message);
    } finally {
      setOpeningServiceOrder(false);
    }
  }

  function handleReset() {
    setSerial('');
    setCnpj('');
    setServiceOrderDefect('');
    setMaintenanceInWarranty(false);
    setPartToSend('');
    setResponse(emptyResponse);
    setError(null);
    setCopied(false);
    setServiceOrderStatus(null);
  }

  return (
    <main className="app-shell">
      <section className="workspace" aria-label="Assistente RMA">
        <header className="topbar">
          <div>
            <p className="eyebrow">Triagem e abertura no UNO</p>
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
          <section className="composer" aria-label="Entrada dos dados">
            <div className="mode-switch" role="tablist" aria-label="Tipo de solicitacao">
              <button
                className={requestMode === 'maintenance' ? 'mode-button active' : 'mode-button'}
                type="button"
                onClick={() => setRequestMode('maintenance')}
                role="tab"
                aria-selected={requestMode === 'maintenance'}
                title="Manutencao"
              >
                <Wrench size={18} />
                <span>Manutencao</span>
              </button>
              <button
                className={requestMode === 'parts' ? 'mode-button active' : 'mode-button'}
                type="button"
                onClick={() => setRequestMode('parts')}
                role="tab"
                aria-selected={requestMode === 'parts'}
                title="Envio de pecas"
              >
                <PackagePlus size={18} />
                <span>Envio de pecas</span>
              </button>
            </div>
            <div className="message incoming">
              <div className="message-header">
                {requestMode === 'maintenance' ? <Wrench size={18} /> : <PackagePlus size={18} />}
                <span>{requestMode === 'maintenance' ? 'Manutencao' : 'Envio de pecas'}</span>
              </div>
              <div className="serial-panel">
                <label htmlFor="cnpj-input">CNPJ da revenda</label>
                <input
                  id="cnpj-input"
                  value={cnpj}
                  onChange={(event) => setCnpj(event.target.value)}
                  placeholder="11222333000181"
                  spellCheck="false"
                  aria-label="Informe o CNPJ da revenda"
                />
                <label htmlFor="serial-input">Series dos equipamentos</label>
                <textarea
                  id="serial-input"
                  value={serial}
                  onChange={(event) => setSerial(event.target.value)}
                  placeholder={`0A0000/000000\n0B0000/000001`}
                  spellCheck="false"
                  aria-label="Informe um ou mais numeros de serie"
                />
                <label htmlFor="service-order-defect">Defeito relatado</label>
                <textarea
                  id="service-order-defect"
                  value={serviceOrderDefect}
                  onChange={(event) => setServiceOrderDefect(event.target.value)}
                  placeholder="Descreva o defeito informado pelo cliente"
                  spellCheck="false"
                  aria-label="Informe o defeito relatado"
                />
                {requestMode === 'maintenance' ? (
                  <label className="checkbox-row" htmlFor="maintenance-warranty">
                    <input
                      id="maintenance-warranty"
                      type="checkbox"
                      checked={maintenanceInWarranty}
                      onChange={(event) => setMaintenanceInWarranty(event.target.checked)}
                    />
                    <span>Manutencao em garantia liberada manualmente</span>
                  </label>
                ) : (
                  <>
                    <label htmlFor="part-input">Peca a ser enviada</label>
                    <input
                      id="part-input"
                      value={partToSend}
                      onChange={(event) => setPartToSend(event.target.value)}
                      placeholder="Informe a peca"
                      spellCheck="false"
                      aria-label="Informe a peca a ser enviada"
                    />
                  </>
                )}
              </div>
            </div>
            <div className="actions">
              <button className="secondary-button" type="button" onClick={handleReset} title="Limpar">
                <RotateCcw size={18} />
                <span>Limpar</span>
              </button>
              <button className="primary-button" type="button" onClick={handleGenerate} disabled={loading || !canSubmit} title="Executar">
                <Send size={18} />
                <span>
                  {loading
                    ? 'Consultando UNO'
                    : requestMode === 'maintenance'
                      ? 'Gerar manutencao'
                      : 'Abrir O.S e gerar template'}
                </span>
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
                  <span>Serie</span>
                  <strong>{extraction?.serial || 'Nao identificado'}</strong>
                </div>
                <div>
                  <span>CNPJ</span>
                  <strong>{extraction?.cnpj || cnpj || 'Nao identificado'}</strong>
                </div>
                <div>
                  <span>Defeito</span>
                  <strong>{extraction?.defeito || serviceOrderDefect || 'Nao identificado'}</strong>
                </div>
              </div>
              {error ? <div className="error-box">{error}</div> : null}
              {response.isHtml ? (
                <div className="html-preview" dangerouslySetInnerHTML={{ __html: response.responseBody }} />
              ) : (
                <pre>{response.responseBody}</pre>
              )}
              {canOpenServiceOrder ? (
                <div className="service-order-prompt">
                  <span>Deseja abrir a O.S de manutencao no UNO?</span>
                  <button type="button" onClick={handleOpenServiceOrder} disabled={openingServiceOrder}>
                    <Send size={18} />
                    <span>{openingServiceOrder ? 'Abrindo O.S' : 'Abrir O.S'}</span>
                  </button>
                  {serviceOrderStatus ? <p>{serviceOrderStatus}</p> : null}
                </div>
              ) : null}
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
