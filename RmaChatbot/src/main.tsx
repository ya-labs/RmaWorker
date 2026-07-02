import React from 'react';
import ReactDOM from 'react-dom/client';
import { CheckCircle2, Clipboard, Download, FileText, Moon, PackagePlus, RefreshCw, RotateCcw, Search, Send, Settings, ShieldAlert, Sun, Wrench } from 'lucide-react';
import './styles.css';
import idSupportLogo from './assets/id-support-logo.png';

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

type SpocIdBlockNextResponse = {
  status: string;
  message: string;
  inputSerial?: string | null;
  baseSerial?: string | null;
  nextSerial?: string | null;
  isHtml: boolean;
  responseBody: string;
};

type InvoiceLookupResponse = {
  status: string;
  message: string;
  invoiceNumber?: string | null;
  fileName?: string | null;
  contentType?: string | null;
  base64Pdf?: string | null;
};

type RequestMode = 'maintenance' | 'parts' | 'exchange' | 'idblock-next' | 'invoice';
type AppTheme = 'light' | 'dark';

const emptyResponse: ApiResponse = {
  status: 'AGUARDANDO',
  isHtml: false,
  responseBody: 'Preencha os dados para consultar o UNO e gerar a resposta.',
  results: [],
};

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL?.replace(/\/$/, '') ?? '';
const unoLoginCookieName = 'rmaworker_uno_login';
const unoPasswordCookieName = 'rmaworker_uno_password';
const themeStorageKey = 'idsupport_theme';

function getInitialTheme(): AppTheme {
  const savedTheme = window.localStorage.getItem(themeStorageKey);

  if (savedTheme === 'light' || savedTheme === 'dark') {
    return savedTheme;
  }

  return 'light';
}

function getCookie(name: string) {
  const prefix = `${name}=`;
  return document.cookie
    .split(';')
    .map((item) => item.trim())
    .find((item) => item.startsWith(prefix))
    ?.slice(prefix.length) ?? '';
}

function setCookie(name: string, value: string) {
  const secure = window.location.protocol === 'https:' ? '; Secure' : '';
  document.cookie = `${name}=${encodeURIComponent(value)}; Max-Age=15552000; Path=/; SameSite=Lax${secure}`;
}

function deleteCookie(name: string) {
  document.cookie = `${name}=; Max-Age=0; Path=/; SameSite=Lax`;
}

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
    SPOC_SERIAL_ENCONTRADO: 'NEXT encontrada',
    SPOC_SERIAL_NAO_ENCONTRADO: 'Nao encontrado no SPOC',
    SPOC_ERRO: 'Erro no SPOC',
    NF_ENCONTRADA: 'NF encontrada',
    NF_NAO_ENCONTRADA: 'NF nao encontrada',
    TECNICO_INVALIDO: 'Tecnico invalido',
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
    return '-';
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

function buildIdBlockNextResult(response: SpocIdBlockNextResponse): ApiResponse {
  return {
    status: response.status,
    isHtml: response.isHtml,
    responseBody: response.responseBody,
    results: [
      {
        extraction: {
          serial: response.nextSerial || response.inputSerial,
        },
        status: response.status,
        reason: response.message,
        missingFields: [],
      },
    ],
  };
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
  const [theme, setTheme] = React.useState<AppTheme>(getInitialTheme);
  const [requestMode, setRequestMode] = React.useState<RequestMode>('maintenance');
  const [serial, setSerial] = React.useState('');
  const [invoiceNumber, setInvoiceNumber] = React.useState('');
  const [cnpj, setCnpj] = React.useState('');
  const [unoLogin, setUnoLogin] = React.useState(() => decodeURIComponent(getCookie(unoLoginCookieName)));
  const [unoPassword, setUnoPassword] = React.useState(() => decodeURIComponent(getCookie(unoPasswordCookieName)));
  const [showSettings, setShowSettings] = React.useState(false);
  const [settingsSaved, setSettingsSaved] = React.useState(false);
  const [serviceOrderDefect, setServiceOrderDefect] = React.useState('');
  const [maintenanceInWarranty, setMaintenanceInWarranty] = React.useState(false);
  const [partToSend, setPartToSend] = React.useState('');
  const [unoObservations, setUnoObservations] = React.useState('');
  const [spocResolution, setSpocResolution] = React.useState<SpocIdBlockNextResponse | null>(null);
  const [invoiceLookup, setInvoiceLookup] = React.useState<InvoiceLookupResponse | null>(null);
  const [response, setResponse] = React.useState<ApiResponse>(emptyResponse);
  const [copied, setCopied] = React.useState(false);
  const [loading, setLoading] = React.useState(false);
  const [openingServiceOrder, setOpeningServiceOrder] = React.useState(false);
  const [serviceOrderStatus, setServiceOrderStatus] = React.useState<string | null>(null);
  const [error, setError] = React.useState<string | null>(null);

  const extraction = response.results.length > 1
    ? { ...firstExtraction(response), serial: serialSummary(response) }
    : firstExtraction(response);
  const isReady = response.status === 'APTO'
    || response.status === 'OS_ABERTA'
    || response.status === 'SPOC_SERIAL_ENCONTRADO'
    || response.status === 'NF_ENCONTRADA';
  const serials = splitSerials(serial);
  const invoicePdfUrl = invoiceLookup?.base64Pdf
    ? `data:${invoiceLookup.contentType || 'application/pdf'};base64,${invoiceLookup.base64Pdf}`
    : '';
  const canSubmit = requestMode === 'invoice'
    ? invoiceNumber.trim().length > 0
    : requestMode === 'idblock-next'
      ? serial.trim().length > 0
      : serials.length > 0
      && cnpj.trim().length > 0
      && serviceOrderDefect.trim().length > 0
      && (requestMode === 'maintenance' || requestMode === 'exchange' || partToSend.trim().length > 0);
  const eligibleResults = response.results.filter((result) => result.status === 'APTO' && result.extraction.serial);
  const canOpenServiceOrder = (requestMode === 'maintenance' || requestMode === 'exchange')
    && eligibleResults.length > 0;
  const resultTitle = requestMode === 'invoice'
    ? 'Nota fiscal'
    : requestMode === 'idblock-next'
      ? 'Consulta SPOC'
      : 'Resposta sugerida';
  const resultAriaLabel = requestMode === 'invoice'
    ? 'Resultado da busca de nota fiscal'
    : requestMode === 'idblock-next'
      ? 'Resultado da consulta IDBlock Next'
      : 'Resposta sugerida';
  const isEmptyResult = response.status === 'AGUARDANDO' && !error;
  const emptyStateTitle = requestMode === 'invoice'
    ? 'Aguardando numero da NF'
    : requestMode === 'idblock-next'
      ? 'Aguardando serial do IDFace'
      : 'Aguardando dados da solicitacao';
  const emptyStateDescription = requestMode === 'invoice'
    ? 'Digite o número da nota fiscal para visualizar o PDF e baixar o arquivo.'
    : requestMode === 'idblock-next'
      ? 'Digite o serial do IDFace para consultar o SPOC e retornar a IDBlock Next.'
      : 'Preencha os campos do formulário para consultar o UNO e montar a resposta.';
  const canCopyResult = requestMode !== 'invoice' && !isEmptyResult;

  React.useEffect(() => {
    window.localStorage.setItem(themeStorageKey, theme);
  }, [theme]);

  async function handleGenerate() {
    setLoading(true);
    setCopied(false);
    setError(null);
    setServiceOrderStatus(null);
    setInvoiceLookup(null);

    try {
      if (requestMode === 'invoice') {
        const apiResponse = await fetch(`${apiBaseUrl}/api/rma/invoice/find`, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
          },
          body: JSON.stringify({
            invoiceNumber,
          }),
        });

        if (!apiResponse.ok) {
          const body = await apiResponse.text();
          throw new Error(body || `Erro HTTP ${apiResponse.status}`);
        }

        const invoiceResponse = await apiResponse.json() as InvoiceLookupResponse;
        setInvoiceLookup(invoiceResponse);
        setResponse({
          status: invoiceResponse.status,
          isHtml: false,
          responseBody: invoiceResponse.message,
          results: [
            {
              extraction: {
                serial: invoiceResponse.invoiceNumber || invoiceNumber,
              },
              status: invoiceResponse.status,
              reason: invoiceResponse.message,
              missingFields: [],
            },
          ],
        });
        return;
      }

      if (requestMode === 'idblock-next') {
        const apiResponse = await fetch(`${apiBaseUrl}/api/rma/spoc/idblock-next/resolve`, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
          },
          body: JSON.stringify({
            serial,
          }),
        });

        if (!apiResponse.ok) {
          const body = await apiResponse.text();
          throw new Error(body || `Erro HTTP ${apiResponse.status}`);
        }

        const spocResponse = await apiResponse.json() as SpocIdBlockNextResponse;
        setSpocResolution(spocResponse);
        setResponse(buildIdBlockNextResult(spocResponse));
        return;
      }

      if (requestMode === 'maintenance' || requestMode === 'exchange') {
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
            requestType: requestMode === 'exchange' ? 'exchange' : 'maintenance',
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
        requestType: type === 'parts' ? 'parts' : type === 'exchange' ? 'exchange' : 'maintenance',
        maintenanceInWarranty,
        partToSend: type === 'parts' ? partToSend : null,
        unoObservations: null,
        unoLogin: unoLogin.trim() || null,
        unoPassword: unoPassword || null,
        items: serials.map((item) => ({
          serial: item,
          defectReported: serviceOrderDefect,
          unoObservations: type === 'maintenance' || type === 'exchange'
              ? unoObservations.trim() || null
              : null,
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
    if (requestMode === 'idblock-next' && spocResolution?.nextSerial) {
      await navigator.clipboard.writeText(spocResolution.nextSerial);
      setCopied(true);
      return;
    }

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

  async function handleOpenExchangeServiceOrder() {
    setOpeningServiceOrder(true);
    setServiceOrderStatus(null);
    setError(null);

    try {
      const serviceOrderResponse = await openServiceOrder('exchange');
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
    setInvoiceNumber('');
    setCnpj('');
    setServiceOrderDefect('');
    setMaintenanceInWarranty(false);
    setPartToSend('');
    setUnoObservations('');
    setSpocResolution(null);
    setInvoiceLookup(null);
    setResponse(emptyResponse);
    setError(null);
    setCopied(false);
    setServiceOrderStatus(null);
  }

  function handleSaveSettings() {
    if (unoLogin.trim()) {
      setCookie(unoLoginCookieName, unoLogin.trim());
    } else {
      deleteCookie(unoLoginCookieName);
    }

    if (unoPassword) {
      setCookie(unoPasswordCookieName, unoPassword);
    } else {
      deleteCookie(unoPasswordCookieName);
    }

    setSettingsSaved(true);
    window.setTimeout(() => setSettingsSaved(false), 2500);
  }

  function handleClearSettings() {
    setUnoLogin('');
    setUnoPassword('');
    deleteCookie(unoLoginCookieName);
    deleteCookie(unoPasswordCookieName);
    setSettingsSaved(false);
  }

  return (
    <main className="app-shell" data-theme={theme}>
      <section className="workspace" aria-label="iDSupport">
        <header className="topbar">
          <div className="topbar-actions">
            <button
              className="theme-toggle"
              type="button"
              onClick={() => setTheme((currentTheme) => currentTheme === 'dark' ? 'light' : 'dark')}
              title={theme === 'dark' ? 'Usar modo claro' : 'Usar modo escuro'}
              aria-label={theme === 'dark' ? 'Usar modo claro' : 'Usar modo escuro'}
            >
              {theme === 'dark' ? <Sun size={18} /> : <Moon size={18} />}
            </button>
            <div className={`status-pill ${isReady ? 'status-ready' : 'status-warning'}`}>
              {isReady ? <CheckCircle2 size={18} /> : <ShieldAlert size={18} />}
              <span>{statusLabel(response.status)}</span>
            </div>
          </div>
        </header>

        <div className="chat-layout">
          <aside className={showSettings ? 'sidebar settings-open' : 'sidebar'} aria-label="Navegacao do assistente">
            <div className="brand-panel">
              <img className="brand-logo" src={idSupportLogo} alt="iDSupport" />
            </div>
            <div className="mode-switch" role="tablist" aria-label="Tipo de solicitacao">
              <button
                className={requestMode === 'maintenance' ? 'mode-button active' : 'mode-button'}
                type="button"
                onClick={() => setRequestMode('maintenance')}
                role="tab"
                aria-selected={requestMode === 'maintenance'}
                title="Manutenção"
              >
                <Wrench size={18} />
                <span>Manutenção</span>
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
              <button
                className={requestMode === 'exchange' ? 'mode-button active' : 'mode-button'}
                type="button"
                onClick={() => setRequestMode('exchange')}
                role="tab"
                aria-selected={requestMode === 'exchange'}
                title="Troca"
              >
                <RefreshCw size={18} />
                <span>Troca</span>
              </button>
              <button
                className={requestMode === 'invoice' ? 'mode-button active' : 'mode-button'}
                type="button"
                onClick={() => {
                  setRequestMode('invoice');
                  setInvoiceLookup(null);
                  setResponse(emptyResponse);
                  setServiceOrderStatus(null);
                  setError(null);
                }}
                role="tab"
                aria-selected={requestMode === 'invoice'}
                title="Buscar NF"
              >
                <FileText size={18} />
                <span>Buscar NF</span>
              </button>
              <button
                className={requestMode === 'idblock-next' ? 'mode-button active' : 'mode-button'}
                type="button"
                onClick={() => {
                  setRequestMode('idblock-next');
                  setSpocResolution(null);
                  setResponse(emptyResponse);
                  setServiceOrderStatus(null);
                  setError(null);
                }}
                role="tab"
                aria-selected={requestMode === 'idblock-next'}
                title="IDBlock Next"
              >
                <Search size={18} />
                <span>IDBlock Next</span>
              </button>
            </div>
            {showSettings ? (
              <section className="settings-popover" aria-label="Configuracoes da integracao">
                <div>
                  <label htmlFor="uno-login-input">Login UNO</label>
                  <input
                    id="uno-login-input"
                    value={unoLogin}
                    onChange={(event) => setUnoLogin(event.target.value)}
                    placeholder="usuario"
                    autoComplete="username"
                    spellCheck="false"
                  />
                </div>
                <div>
                  <label htmlFor="uno-password-input">Senha UNO</label>
                  <input
                    id="uno-password-input"
                    type="password"
                    value={unoPassword}
                    onChange={(event) => setUnoPassword(event.target.value)}
                    placeholder="senha"
                    autoComplete="current-password"
                  />
                </div>
                <div className="settings-actions">
                  <button className="settings-mini-button save" type="button" onClick={handleSaveSettings}>
                    <Settings size={17} />
                    <span>Salvar</span>
                  </button>
                  <button className="settings-mini-button clear" type="button" onClick={handleClearSettings}>
                    <RotateCcw size={17} />
                    <span>Limpar</span>
                  </button>
                </div>
              </section>
            ) : null}
            <button className="settings-button" type="button" onClick={() => setShowSettings((value) => !value)} title="Configuracoes">
              <Settings size={18} />
              <span>Configuracoes</span>
            </button>
          </aside>

          <section className="composer" aria-label="Entrada dos dados">
            <div className="message incoming flow-card">
              <div className="message-header">
                {requestMode === 'maintenance' ? <Wrench size={18} /> : requestMode === 'parts' ? <PackagePlus size={18} /> : requestMode === 'exchange' ? <RefreshCw size={18} /> : requestMode === 'invoice' ? <FileText size={18} /> : <Search size={18} />}
                <span>{requestMode === 'maintenance' ? 'Manutenção' : requestMode === 'parts' ? 'Envio de pecas' : requestMode === 'exchange' ? 'Troca' : requestMode === 'invoice' ? 'Buscar NF' : 'IDBlock Next'}</span>
              </div>
              <div className="serial-panel">
                {requestMode === 'invoice' ? (
                  <>
                    <label htmlFor="invoice-number-input">Número</label>
                    <input
                      id="invoice-number-input"
                      value={invoiceNumber}
                      onChange={(event) => setInvoiceNumber(event.target.value)}
                      placeholder="Informe o número da NF"
                      inputMode="numeric"
                      spellCheck="false"
                      aria-label="Informe o número da nota fiscal"
                    />
                  </>
                ) : null}
                {requestMode === 'idblock-next' || requestMode === 'invoice' ? null : (
                  <>
                    <label htmlFor="cnpj-input">CNPJ da revenda</label>
                    <input
                      id="cnpj-input"
                      value={cnpj}
                      onChange={(event) => setCnpj(event.target.value)}
                      placeholder="11222333000181"
                      spellCheck="false"
                      aria-label="Informe o CNPJ da revenda"
                    />
                  </>
                )}
                {requestMode === 'invoice' ? null : (
                  <label htmlFor="serial-input">{requestMode === 'idblock-next' ? 'Serial do IDFace' : 'Número de série dos equipamentos'}</label>
                )}
                {requestMode === 'invoice' ? null : requestMode === 'idblock-next' ? (
                  <input
                    id="serial-input"
                    value={serial}
                    onChange={(event) => setSerial(event.target.value)}
                    placeholder="0A0000/000000"
                    spellCheck="false"
                    aria-label="Informe o numero de serie do IDFace"
                  />
                ) : (
                  <textarea
                    id="serial-input"
                    value={serial}
                    onChange={(event) => setSerial(event.target.value)}
                    placeholder={`0A0000/000000\n0B0000/000001`}
                    spellCheck="false"
                    aria-label="Informe um ou mais numeros de serie"
                  />
                )}
                {requestMode !== 'idblock-next' && requestMode !== 'invoice' ? (
                  <>
                    <label htmlFor="service-order-defect">Defeito relatado</label>
                    <textarea
                      id="service-order-defect"
                      value={serviceOrderDefect}
                      onChange={(event) => setServiceOrderDefect(event.target.value)}
                      placeholder="Descreva o defeito informado pelo cliente"
                      spellCheck="false"
                      aria-label="Informe o defeito relatado"
                    />
                    {requestMode === 'maintenance' || requestMode === 'exchange' ? (
                      <>
                        <label htmlFor="uno-observations">Observações</label>
                        <textarea
                          id="uno-observations"
                          value={unoObservations}
                          onChange={(event) => setUnoObservations(event.target.value)}
                          placeholder="Observações para a O.S"
                          spellCheck="false"
                          aria-label="Informe observações para a O.S"
                        />
                      </>
                    ) : null}
                    <label className="checkbox-row" htmlFor="maintenance-warranty">
                      <input
                        id="maintenance-warranty"
                        type="checkbox"
                        checked={maintenanceInWarranty}
                        onChange={(event) => setMaintenanceInWarranty(event.target.checked)}
                      />
                      <span>{requestMode === 'parts' ? 'Envio de pecas' : requestMode === 'exchange' ? 'Troca' : 'Manutenção'} em garantia liberada manualmente</span>
                    </label>
                  </>
                ) : null}
                {requestMode === 'parts' ? (
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
                ) : null}
              </div>
            </div>
            <div className="actions">
              <button className="secondary-button" type="button" onClick={handleReset} title="Limpar">
                <RotateCcw size={18} />
                <span>Limpar</span>
              </button>
              <button className="primary-button" type="button" onClick={handleGenerate} disabled={loading || !canSubmit} title="Executar">
                {requestMode === 'idblock-next' ? <Search size={18} /> : requestMode === 'invoice' ? <FileText size={18} /> : <Send size={18} />}
                <span>
                  {loading
                    ? requestMode === 'idblock-next' ? 'Consultando SPOC' : requestMode === 'invoice' ? 'Buscando NF' : 'Consultando UNO'
                    : requestMode === 'maintenance'
                      ? 'Gerar manutenção'
                      : requestMode === 'exchange'
                        ? 'Gerar troca'
                        : requestMode === 'parts'
                          ? 'Abrir O.S e gerar template'
                          : requestMode === 'invoice'
                            ? 'Buscar NF'
                          : 'Consultar SPOC'}
                </span>
              </button>
            </div>
          </section>

          <section className="result" aria-label={resultAriaLabel}>
            <div className="message outgoing result-card">
              <div className="message-header">
                {requestMode === 'invoice' ? <FileText size={18} /> : requestMode === 'idblock-next' ? <Search size={18} /> : <Clipboard size={18} />}
                <span>{resultTitle}</span>
              </div>
              {!isEmptyResult ? (
                <div className="extracted-grid">
                  <div>
                    <span>{requestMode === 'invoice' ? 'Nota fiscal' : requestMode === 'idblock-next' ? 'Serial consultado' : 'Série'}</span>
                    <strong>{requestMode === 'invoice' ? invoiceLookup?.invoiceNumber || invoiceNumber || '-' : requestMode === 'idblock-next' ? spocResolution?.inputSerial || serial || '-' : extraction?.serial || '-'}</strong>
                  </div>
                  <div>
                    <span>{requestMode === 'invoice' ? 'Arquivo' : requestMode === 'idblock-next' ? 'Serial IDBlock Next' : 'CNPJ'}</span>
                    <strong>{requestMode === 'invoice' ? invoiceLookup?.fileName || '-' : requestMode === 'idblock-next' ? spocResolution?.nextSerial || '-' : extraction?.cnpj || cnpj || '-'}</strong>
                  </div>
                  <div>
                    <span>{requestMode === 'invoice' || requestMode === 'idblock-next' ? 'Status' : 'Defeito'}</span>
                    <strong>{requestMode === 'invoice' || requestMode === 'idblock-next' ? statusLabel(response.status) : extraction?.defeito || serviceOrderDefect || '-'}</strong>
                  </div>
                </div>
              ) : null}
              {requestMode === 'idblock-next' && spocResolution?.nextSerial ? (
                <div className="next-serial-box">
                  <span>Serial da IDBlock Next encontrado no SPOC</span>
                  <strong>{spocResolution.nextSerial}</strong>
                </div>
              ) : null}
              {requestMode === 'invoice' && invoiceLookup?.base64Pdf && invoicePdfUrl ? (
                <div className="invoice-preview">
                  <iframe title="Visualizacao da nota fiscal" src={invoicePdfUrl} />
                  <a
                    className="download-button"
                    href={invoicePdfUrl}
                    download={invoiceLookup.fileName || `nota-fiscal-${invoiceLookup.invoiceNumber || invoiceNumber}.pdf`}
                  >
                    <Download size={18} />
                    <span>Baixar NF</span>
                  </a>
                </div>
              ) : null}
              {error ? <div className="error-box">{error}</div> : null}
              {response.isHtml ? (
                <div className="html-preview" dangerouslySetInnerHTML={{ __html: response.responseBody }} />
              ) : isEmptyResult ? (
                <div className="empty-state">
                  <div className="empty-state-icon">
                    {requestMode === 'invoice' ? <FileText size={28} /> : requestMode === 'idblock-next' ? <Search size={28} /> : <Clipboard size={28} />}
                  </div>
                  <strong>{emptyStateTitle}</strong>
                  <span>{emptyStateDescription}</span>
                </div>
              ) : (
                <pre>{response.responseBody}</pre>
              )}
              {canOpenServiceOrder ? (
                <div className="service-order-prompt">
                  <span>{requestMode === 'exchange' ? 'Deseja abrir a O.S de troca no UNO?' : 'Deseja abrir a O.S de manutenção no UNO?'}</span>
                  <button type="button" onClick={requestMode === 'exchange' ? handleOpenExchangeServiceOrder : handleOpenServiceOrder} disabled={openingServiceOrder}>
                    {requestMode === 'exchange' ? <RefreshCw size={18} /> : <Wrench size={18} />}
                    <span>{openingServiceOrder ? 'Abrindo O.S' : 'Abrir O.S'}</span>
                  </button>
                  {serviceOrderStatus ? <p>{serviceOrderStatus}</p> : null}
                </div>
              ) : null}
              {canCopyResult ? (
                <button className="copy-button" type="button" onClick={handleCopy} title={requestMode === 'idblock-next' ? 'Copiar serial' : 'Copiar resposta'}>
                  <Clipboard size={18} />
                  <span>{copied ? 'Copiado' : requestMode === 'idblock-next' ? 'Copiar serial' : 'Copiar resposta'}</span>
                </button>
              ) : null}
            </div>
          </section>
        </div>
      </section>
    </main>
  );
}

ReactDOM.createRoot(document.getElementById('root')!).render(<App />);
