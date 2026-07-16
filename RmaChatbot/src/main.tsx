import React from 'react';
import ReactDOM from 'react-dom/client';
import { createPortal } from 'react-dom';
import { CheckCircle2, Clipboard, Download, FileText, Moon, PackagePlus, PanelLeftClose, PanelLeftOpen, RefreshCw, RotateCcw, Search, Send, Settings, ShieldAlert, Sun, Wrench } from 'lucide-react';
import './styles.css';
import idIcon from './assets/id-icon.png';
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

type OccurrenceOpenResponse = {
  status: string;
  message: string;
  occurrenceCode?: string | null;
  customerCode?: string | null;
  customerName?: string | null;
  categoryCode?: string | null;
  title?: string | null;
};

type OccurrenceDraft = {
  id: string;
  title: string;
  description: string;
  categoryCode: string;
  occurrenceTypeCode: string;
  statusCode: string;
  costCenterCode: string;
  cnpj: string;
  status: 'RASCUNHO' | 'ABERTA_NO_UNO' | 'ERRO_AO_ABRIR';
  occurrenceCode?: string | null;
  updatedAt: string;
};

type RequestMode = 'maintenance' | 'parts' | 'exchange' | 'occurrence' | 'idblock-next' | 'invoice';
type AppTheme = 'light' | 'dark';
type TooltipPlacement = 'top' | 'bottom';

type ActiveTooltip = {
  description: string;
  left: number;
  top: number;
  maxWidth: number;
  placement: TooltipPlacement;
};

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
const formPaneWidthStorageKey = 'idsupport_form_pane_width';
const sidebarCollapsedStorageKey = 'idsupport_sidebar_collapsed';
const occurrenceDraftsStoragePrefix = 'idsupport_occurrence_drafts';
const defaultOccurrenceTypeCode = '1';
const defaultOccurrenceStatusCode = '50';
const defaultOccurrenceCostCenterCode = '14';
const defaultFormPaneWidth = 430;
const minFormPaneWidth = 280;
const maxFormPaneWidth = 1040;
const expandedSidebarWidth = 236;
const collapsedSidebarWidth = 76;
const paneResizerWidth = 8;
const minResultPaneWidth = 320;
const occurrenceCategoryOptions = [
  { code: '12', name: 'Catraca' },
  { code: '40', name: 'Catraca Facial' },
  { code: '48', name: 'Catraca Next' },
  { code: '41', name: 'Genetec' },
  { code: '13', name: 'iDAccess Nano ou Pro' },
  { code: '14', name: 'iDAccess ou iDFit' },
  { code: '15', name: 'iDBio' },
  { code: '18', name: 'iDBox' },
  { code: '46', name: 'iDConnect' },
  { code: '37', name: 'iDFace' },
  { code: '52', name: 'iDFace MAX' },
  { code: '11', name: 'iDFlex' },
  { code: '19', name: 'iDLock' },
  { code: '42', name: 'iDPower' },
  { code: '20', name: 'iDProx' },
  { code: '38', name: 'iDProx USB' },
  { code: '17', name: 'iDTouch' },
  { code: '32', name: 'iDUHF' },
  { code: '16', name: 'Outros' },
  { code: '8', name: 'iDSecure' },
  { code: '45', name: 'iDSecure Cloud' },
];
const occurrenceTypeOptions = [
  { code: '1', name: 'Dúvida de utilização' },
  { code: '2', name: 'RMA e envio de peça' },
  { code: '3', name: 'Problemas de produto' },
  { code: '4', name: 'Licença' },
  { code: '5', name: 'Outros' },
  { code: '6', name: 'Visita manutenção' },
  { code: '7', name: 'Visita treinamento' },
  { code: '8', name: 'Instalação' },
  { code: '9', name: 'Exportação' },
  { code: '10', name: 'Treinamento Remoto' },
];
const occurrenceStatusOptions = [
  { code: '10', name: 'Aberto' },
  { code: '20', name: 'Engenharia' },
  { code: '50', name: 'Resolvido' },
  { code: '70', name: 'Pendente cliente' },
  { code: '90', name: '-' },
];
const occurrenceCostCenterOptions = [
  { code: '208', name: 'Comex - ENRUPI' },
  { code: '2023', name: 'Comex - HUAYU' },
  { code: '10', name: 'Filial' },
  { code: '11', name: 'Agenciamento Automação' },
  { code: '12', name: 'Mensalistas - Final' },
  { code: '13', name: 'Mensalistas - Revenda - RHID' },
  { code: '14', name: 'Suporte - Acesso' },
  { code: '15', name: 'Suporte - Ponto e Automacao' },
  { code: '16', name: 'Suporte Ponto - Final' },
  { code: '17', name: 'Redução Plano - Covid 19' },
  { code: '18', name: 'Representante - Fechadura' },
  { code: '19', name: 'Onboarding' },
  { code: '2', name: 'Revendas' },
  { code: '20', name: 'Exportação Assa Abloy' },
  { code: '21', name: 'Suporte Acesso - Final' },
  { code: '22', name: 'Suporte Projetos - Final' },
  { code: '23', name: 'Mensalistas - Revenda - iDSecure Cloud' },
  { code: '24', name: 'Multa cancelamento de contrato' },
  { code: '25', name: 'Variação cambial' },
  { code: '26', name: 'Assistência técnica SP Revendas' },
  { code: '27', name: 'YALE' },
  { code: '4', name: 'Cliente Final' },
  { code: '5', name: 'Venda Direta Revenda' },
  { code: '6', name: 'Revenda Manutenção' },
  { code: '7', name: 'Exportação' },
  { code: '8', name: 'Acesso' },
  { code: '9', name: 'Automação Comercial' },
];

function clamp(value: number, min: number, max: number) {
  return Math.min(Math.max(value, min), max);
}

function getInitialTheme(): AppTheme {
  const savedTheme = window.localStorage.getItem(themeStorageKey);

  if (savedTheme === 'light' || savedTheme === 'dark') {
    return savedTheme;
  }

  return 'light';
}

function getMaxFormPaneWidth(sidebarWidth = expandedSidebarWidth) {
  if (typeof window === 'undefined') {
    return maxFormPaneWidth;
  }

  const availableWidth = window.innerWidth - sidebarWidth - paneResizerWidth - minResultPaneWidth;
  return Math.max(minFormPaneWidth, Math.min(maxFormPaneWidth, availableWidth));
}

function getInitialFormPaneWidth() {
  const savedWidth = Number(window.localStorage.getItem(formPaneWidthStorageKey));

  if (Number.isFinite(savedWidth) && savedWidth > 0) {
    return clamp(savedWidth, minFormPaneWidth, getMaxFormPaneWidth());
  }

  return clamp(defaultFormPaneWidth, minFormPaneWidth, getMaxFormPaneWidth());
}

function getInitialSidebarCollapsed() {
  return window.localStorage.getItem(sidebarCollapsedStorageKey) === 'true';
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
    OC_RASCUNHO: 'Rascunho salvo',
    OC_ABERTA: 'O.C aberta',
    OC_ERRO: 'Erro na O.C',
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

function InfoTooltip({
  description,
  onShow,
  onHide,
}: {
  description: string;
  onShow: (description: string, anchor: HTMLElement) => void;
  onHide: () => void;
}) {
  const iconRef = React.useRef<HTMLSpanElement | null>(null);

  function handleShow() {
    if (iconRef.current) {
      onShow(description, iconRef.current);
    }
  }

  return (
    <span
      ref={iconRef}
      className="field-info"
      aria-label={description}
      tabIndex={0}
      onMouseEnter={handleShow}
      onMouseLeave={onHide}
      onFocus={handleShow}
      onBlur={onHide}
      onClick={(event) => event.preventDefault()}
    >
      ⓘ
    </span>
  );
}

function occurrenceDraftsStorageKey(login: string) {
  const owner = login.trim().toLowerCase() || 'local';
  return `${occurrenceDraftsStoragePrefix}_${owner}`;
}

function loadOccurrenceDrafts(login: string): OccurrenceDraft[] {
  try {
    const raw = window.localStorage.getItem(occurrenceDraftsStorageKey(login));
    if (!raw) {
      return [];
    }

    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

function saveOccurrenceDrafts(login: string, drafts: OccurrenceDraft[]) {
  window.localStorage.setItem(occurrenceDraftsStorageKey(login), JSON.stringify(drafts));
}

function createDraftId() {
  return typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID()
    : `${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

function FieldLabel({
  htmlFor,
  children,
  description,
  onTooltipShow,
  onTooltipHide,
}: {
  htmlFor: string;
  children: React.ReactNode;
  description: string;
  onTooltipShow: (description: string, anchor: HTMLElement) => void;
  onTooltipHide: () => void;
}) {
  return (
    <label className="field-label" htmlFor={htmlFor}>
      <span>{children}</span>
      <InfoTooltip description={description} onShow={onTooltipShow} onHide={onTooltipHide} />
    </label>
  );
}

function App() {
  const [theme, setTheme] = React.useState<AppTheme>(getInitialTheme);
  const [formPaneWidth, setFormPaneWidth] = React.useState(getInitialFormPaneWidth);
  const [isResizingPane, setIsResizingPane] = React.useState(false);
  const [sidebarCollapsed, setSidebarCollapsed] = React.useState(getInitialSidebarCollapsed);
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
  const [occurrenceTitle, setOccurrenceTitle] = React.useState('');
  const [occurrenceDescription, setOccurrenceDescription] = React.useState('');
  const [occurrenceCategoryCode, setOccurrenceCategoryCode] = React.useState('');
  const [occurrenceTypeCode, setOccurrenceTypeCode] = React.useState(defaultOccurrenceTypeCode);
  const [occurrenceStatusCode, setOccurrenceStatusCode] = React.useState(defaultOccurrenceStatusCode);
  const [occurrenceCostCenterCode, setOccurrenceCostCenterCode] = React.useState(defaultOccurrenceCostCenterCode);
  const [occurrenceDrafts, setOccurrenceDrafts] = React.useState<OccurrenceDraft[]>(() => loadOccurrenceDrafts(decodeURIComponent(getCookie(unoLoginCookieName))));
  const [selectedOccurrenceDraftId, setSelectedOccurrenceDraftId] = React.useState<string | null>(null);
  const [lastOccurrence, setLastOccurrence] = React.useState<OccurrenceOpenResponse | null>(null);
  const [spocResolution, setSpocResolution] = React.useState<SpocIdBlockNextResponse | null>(null);
  const [invoiceLookup, setInvoiceLookup] = React.useState<InvoiceLookupResponse | null>(null);
  const [response, setResponse] = React.useState<ApiResponse>(emptyResponse);
  const [copied, setCopied] = React.useState(false);
  const [loading, setLoading] = React.useState(false);
  const [openingServiceOrder, setOpeningServiceOrder] = React.useState(false);
  const [serviceOrderStatus, setServiceOrderStatus] = React.useState<string | null>(null);
  const [error, setError] = React.useState<string | null>(null);
  const [activeTooltip, setActiveTooltip] = React.useState<ActiveTooltip | null>(null);
  const layoutRef = React.useRef<HTMLDivElement | null>(null);

  const extraction = response.results.length > 1
    ? { ...firstExtraction(response), serial: serialSummary(response) }
    : firstExtraction(response);
  const isServiceOrderMode = requestMode === 'maintenance' || requestMode === 'parts' || requestMode === 'exchange';
  const isReady = response.status === 'APTO'
    || response.status === 'OS_ABERTA'
    || response.status === 'SPOC_SERIAL_ENCONTRADO'
    || response.status === 'NF_ENCONTRADA'
    || response.status === 'OC_ABERTA'
    || response.status === 'OC_RASCUNHO';
  const serials = splitSerials(serial);
  const hasOccurrenceCredentials = unoLogin.trim().length > 0 && unoPassword.length > 0;
  const invoicePdfUrl = invoiceLookup?.base64Pdf
    ? `data:${invoiceLookup.contentType || 'application/pdf'};base64,${invoiceLookup.base64Pdf}`
    : '';
  const canSubmit = requestMode === 'invoice'
    ? invoiceNumber.trim().length > 0
    : requestMode === 'idblock-next'
      ? serial.trim().length > 0
      : requestMode === 'occurrence'
        ? occurrenceTitle.trim().length > 0
          && occurrenceDescription.trim().length > 0
          && occurrenceCategoryCode.trim().length > 0
          && hasOccurrenceCredentials
        : serials.length > 0
          && cnpj.trim().length > 0
          && serviceOrderDefect.trim().length > 0
          && (requestMode === 'maintenance' || requestMode === 'exchange' || partToSend.trim().length > 0);
  const canSaveOccurrenceDraft = requestMode === 'occurrence'
    && [occurrenceTitle, occurrenceDescription, occurrenceCategoryCode, occurrenceTypeCode, occurrenceStatusCode, occurrenceCostCenterCode, cnpj]
      .some((value) => value.trim().length > 0);
  const eligibleResults = response.results.filter((result) => result.status === 'APTO' && result.extraction.serial);
  const canOpenServiceOrder = (requestMode === 'maintenance' || requestMode === 'exchange')
    && eligibleResults.length > 0;
  const resultTitle = requestMode === 'invoice'
    ? 'Nota fiscal'
    : requestMode === 'occurrence'
      ? 'Ocorrência'
    : requestMode === 'idblock-next'
      ? 'Consulta SPOC'
      : 'Resposta sugerida';
  const resultAriaLabel = requestMode === 'invoice'
    ? 'Resultado da busca de nota fiscal'
    : requestMode === 'occurrence'
      ? 'Resultado da ocorrência'
    : requestMode === 'idblock-next'
      ? 'Resultado da consulta IDBlock Next'
      : 'Resposta sugerida';
  const isEmptyResult = response.status === 'AGUARDANDO' && !error;
  const emptyStateTitle = requestMode === 'invoice'
    ? 'Aguardando numero da NF'
    : requestMode === 'occurrence'
      ? 'Aguardando ocorrência'
    : requestMode === 'idblock-next'
      ? 'Aguardando serial do IDFace'
      : 'Aguardando dados da solicitacao';
  const emptyStateDescription = requestMode === 'invoice'
    ? 'Digite o número da nota fiscal para visualizar o PDF e baixar o arquivo.'
    : requestMode === 'occurrence'
      ? 'Preencha a ocorrência durante o atendimento e finalize para abrir a O.C no UNO.'
    : requestMode === 'idblock-next'
      ? 'Digite o serial do IDFace para consultar o SPOC e retornar a IDBlock Next.'
      : 'Preencha os campos do formulário para consultar o UNO e montar a resposta.';
  const canCopyResult = requestMode !== 'invoice' && requestMode !== 'occurrence' && !isEmptyResult;
  const isBusy = loading || openingServiceOrder;
  const shouldShowInvoicePreview = requestMode === 'invoice' && Boolean(invoiceLookup?.base64Pdf && invoicePdfUrl);
  const shouldShowExtractedGrid = !isEmptyResult && requestMode !== 'invoice' && requestMode !== 'occurrence';
  const responseTone = error || response.status === 'ERRO' || response.status.includes('ERRO') || response.status === 'failed'
    ? 'error'
    : isReady
      ? 'success'
      : 'warning';
  const responseCardTitle = responseTone === 'error'
    ? 'Não foi possível concluir'
    : responseTone === 'success'
      ? statusLabel(response.status)
      : 'Retorno da solicitação';
  const responseCardMessage = error || response.responseBody;
  const busyTitle = loading
    ? requestMode === 'invoice'
      ? 'Buscando nota fiscal'
      : requestMode === 'occurrence'
        ? 'Abrindo ocorrência'
      : requestMode === 'idblock-next'
        ? 'Consultando SPOC'
        : 'Consultando UNO'
    : 'Abrindo O.S no UNO';
  const busyDescription = loading
    ? requestMode === 'invoice'
      ? 'Aguarde enquanto o aplicativo acessa o UNO e prepara a visualização do PDF.'
      : requestMode === 'occurrence'
        ? 'Aguarde enquanto o aplicativo registra a O.C no UNO.'
      : requestMode === 'idblock-next'
        ? 'Aguarde enquanto o aplicativo consulta o serial no SPOC.'
        : 'Aguarde enquanto o aplicativo valida os dados e monta a resposta.'
    : 'Aguarde enquanto a O.S é registrada no UNO.';
  const warrantyHelpText = requestMode === 'exchange'
    ? 'Use quando a troca precisar ser liberada manualmente: considera o equipamento em garantia mesmo se a validação automática indicar prazo vencido.'
    : requestMode === 'parts'
      ? 'Use quando o envio de peças precisar ser liberado manualmente: força a abertura como garantia mesmo se a validação automática indicar prazo vencido.'
      : 'Use quando a manutenção precisar ser liberada manualmente: força a abertura como garantia mesmo se a validação automática indicar prazo vencido.';
  const modeTitle = requestMode === 'maintenance'
    ? 'Manutenção'
    : requestMode === 'parts'
      ? 'Envio de pecas'
      : requestMode === 'exchange'
        ? 'Troca'
        : requestMode === 'occurrence'
          ? 'Ocorrências'
          : requestMode === 'invoice'
            ? 'Buscar NF'
            : 'IDBlock Next';

  React.useEffect(() => {
    window.localStorage.setItem(themeStorageKey, theme);
  }, [theme]);

  React.useEffect(() => {
    setOccurrenceDrafts(loadOccurrenceDrafts(unoLogin));
    setSelectedOccurrenceDraftId(null);
  }, [unoLogin]);

  React.useEffect(() => {
    function hideFloatingTooltip() {
      setActiveTooltip(null);
    }

    window.addEventListener('resize', hideFloatingTooltip);
    window.addEventListener('scroll', hideFloatingTooltip, true);

    return () => {
      window.removeEventListener('resize', hideFloatingTooltip);
      window.removeEventListener('scroll', hideFloatingTooltip, true);
    };
  }, []);

  function showFloatingTooltip(description: string, anchor: HTMLElement) {
    const rect = anchor.getBoundingClientRect();
    const maxWidth = Math.min(340, Math.max(220, window.innerWidth - 24));
    const left = clamp(rect.left + rect.width / 2, 12 + maxWidth / 2, window.innerWidth - 12 - maxWidth / 2);
    const shouldOpenAbove = window.innerHeight - rect.bottom < 150 && rect.top > 150;

    setActiveTooltip({
      description,
      left,
      top: shouldOpenAbove ? rect.top - 10 : rect.bottom + 10,
      maxWidth,
      placement: shouldOpenAbove ? 'top' : 'bottom',
    });
  }

  function hideFloatingTooltip() {
    setActiveTooltip(null);
  }

  React.useEffect(() => {
    window.localStorage.setItem(sidebarCollapsedStorageKey, String(sidebarCollapsed));
    setFormPaneWidth((currentWidth) => clamp(
      currentWidth,
      minFormPaneWidth,
      getMaxFormPaneWidth(sidebarCollapsed ? collapsedSidebarWidth : expandedSidebarWidth),
    ));
  }, [sidebarCollapsed]);

  React.useEffect(() => {
    if (window.innerWidth > 1180) {
      window.localStorage.setItem(formPaneWidthStorageKey, String(Math.round(formPaneWidth)));
    }
  }, [formPaneWidth]);

  React.useEffect(() => {
    function handleWindowResize() {
      setFormPaneWidth((currentWidth) => clamp(
        currentWidth,
        minFormPaneWidth,
        getMaxFormPaneWidth(sidebarCollapsed ? collapsedSidebarWidth : expandedSidebarWidth),
      ));
    }

    window.addEventListener('resize', handleWindowResize);

    return () => window.removeEventListener('resize', handleWindowResize);
  }, [sidebarCollapsed]);

  function updateFormPaneWidth(nextWidth: number) {
    setFormPaneWidth(clamp(
      nextWidth,
      minFormPaneWidth,
      getMaxFormPaneWidth(sidebarCollapsed ? collapsedSidebarWidth : expandedSidebarWidth),
    ));
  }

  function handleModeChange(mode: RequestMode) {
    setRequestMode(mode);
    setResponse(emptyResponse);
    setCopied(false);
    setError(null);
    setServiceOrderStatus(null);

    if (mode !== 'invoice') {
      setInvoiceLookup(null);
    }

    if (mode !== 'idblock-next') {
      setSpocResolution(null);
    }

    if (mode !== 'occurrence') {
      setLastOccurrence(null);
    }
  }

  function persistOccurrenceDrafts(nextDrafts: OccurrenceDraft[]) {
    setOccurrenceDrafts(nextDrafts);
    saveOccurrenceDrafts(unoLogin, nextDrafts);
  }

  function buildCurrentOccurrenceDraft(status: OccurrenceDraft['status'] = 'RASCUNHO', occurrenceCode?: string | null): OccurrenceDraft {
    return {
      id: selectedOccurrenceDraftId || createDraftId(),
      title: occurrenceTitle.trim(),
      description: occurrenceDescription.trim(),
      categoryCode: occurrenceCategoryCode.trim(),
      occurrenceTypeCode,
      statusCode: occurrenceStatusCode,
      costCenterCode: occurrenceCostCenterCode,
      cnpj: cnpj.trim(),
      status,
      occurrenceCode,
      updatedAt: new Date().toISOString(),
    };
  }

  function handleSaveOccurrenceDraft() {
    if (!canSaveOccurrenceDraft) {
      return;
    }

    const draft = buildCurrentOccurrenceDraft();
    const nextDrafts = [
      draft,
      ...occurrenceDrafts.filter((item) => item.id !== draft.id),
    ];

    persistOccurrenceDrafts(nextDrafts);
    setSelectedOccurrenceDraftId(draft.id);
    setResponse({
      status: 'OC_RASCUNHO',
      isHtml: false,
      responseBody: 'Rascunho de ocorrência salvo neste navegador para o login configurado.',
      results: [],
    });
  }

  function handleSelectOccurrenceDraft(draft: OccurrenceDraft) {
    setSelectedOccurrenceDraftId(draft.id);
    setOccurrenceTitle(draft.title);
    setOccurrenceDescription(draft.description);
    setOccurrenceCategoryCode(draft.categoryCode);
    setOccurrenceTypeCode(draft.occurrenceTypeCode || defaultOccurrenceTypeCode);
    setOccurrenceStatusCode(draft.statusCode || defaultOccurrenceStatusCode);
    setOccurrenceCostCenterCode(draft.costCenterCode || defaultOccurrenceCostCenterCode);
    setCnpj(draft.cnpj);
    setLastOccurrence(draft.occurrenceCode
      ? {
        status: 'OC_ABERTA',
        message: `Ocorrência ${draft.occurrenceCode} aberta no UNO.`,
        occurrenceCode: draft.occurrenceCode,
        customerCode: null,
        customerName: null,
        categoryCode: draft.categoryCode,
        title: draft.title,
      }
      : null);
    setResponse({
      status: draft.status === 'ABERTA_NO_UNO' ? 'OC_ABERTA' : draft.status === 'ERRO_AO_ABRIR' ? 'OC_ERRO' : 'OC_RASCUNHO',
      isHtml: false,
      responseBody: draft.status === 'ABERTA_NO_UNO'
        ? `Ocorrência ${draft.occurrenceCode} aberta no UNO.`
        : draft.status === 'ERRO_AO_ABRIR'
          ? 'Este rascunho teve erro ao abrir no UNO. Revise os dados e tente novamente.'
          : 'Rascunho carregado para edição.',
      results: [],
    });
  }

  function handleDeleteOccurrenceDraft(id: string) {
    const nextDrafts = occurrenceDrafts.filter((draft) => draft.id !== id);
    persistOccurrenceDrafts(nextDrafts);
    if (selectedOccurrenceDraftId === id) {
      setSelectedOccurrenceDraftId(null);
    }
  }

  function handlePaneResizeStart(event: React.PointerEvent<HTMLDivElement>) {
    event.preventDefault();

    const startX = event.clientX;
    const startWidth = formPaneWidth;
    let nextWidth = startWidth;
    let animationFrame = 0;
    setIsResizingPane(true);

    function applyWidth(width: number) {
      nextWidth = clamp(
        width,
        minFormPaneWidth,
        getMaxFormPaneWidth(sidebarCollapsed ? collapsedSidebarWidth : expandedSidebarWidth),
      );

      if (animationFrame) {
        return;
      }

      animationFrame = window.requestAnimationFrame(() => {
        layoutRef.current?.style.setProperty('--form-pane-width', `${nextWidth}px`);
        animationFrame = 0;
      });
    }

    function handlePointerMove(pointerEvent: PointerEvent) {
      applyWidth(startWidth + pointerEvent.clientX - startX);
    }

    function handlePointerUp() {
      if (animationFrame) {
        window.cancelAnimationFrame(animationFrame);
      }

      layoutRef.current?.style.setProperty('--form-pane-width', `${nextWidth}px`);
      setFormPaneWidth(nextWidth);
      setIsResizingPane(false);
      window.removeEventListener('pointermove', handlePointerMove);
      window.removeEventListener('pointerup', handlePointerUp);
    }

    window.addEventListener('pointermove', handlePointerMove);
    window.addEventListener('pointerup', handlePointerUp);
  }

  function handlePaneResizeKeyDown(event: React.KeyboardEvent<HTMLDivElement>) {
    if (event.key === 'ArrowLeft') {
      event.preventDefault();
      updateFormPaneWidth(formPaneWidth - 24);
    }

    if (event.key === 'ArrowRight') {
      event.preventDefault();
      updateFormPaneWidth(formPaneWidth + 24);
    }

    if (event.key === 'Home') {
      event.preventDefault();
      updateFormPaneWidth(minFormPaneWidth);
    }

    if (event.key === 'End') {
      event.preventDefault();
      updateFormPaneWidth(getMaxFormPaneWidth(sidebarCollapsed ? collapsedSidebarWidth : expandedSidebarWidth));
    }
  }

  async function handleGenerate() {
    setLoading(true);
    setCopied(false);
    setError(null);
    setServiceOrderStatus(null);
    setInvoiceLookup(null);

    try {
      if (requestMode === 'occurrence') {
        if (!hasOccurrenceCredentials) {
          setResponse({
            status: 'OC_ERRO',
            isHtml: false,
            responseBody: 'Configure o login e a senha do UNO antes de finalizar a ocorrência.',
            results: [],
          });
          return;
        }

        const apiResponse = await fetch(`${apiBaseUrl}/api/rma/occurrence/open`, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
          },
          body: JSON.stringify({
            title: occurrenceTitle,
            description: occurrenceDescription,
            categoryCode: occurrenceCategoryCode,
            occurrenceTypeCode,
            statusCode: occurrenceStatusCode,
            costCenterCode: occurrenceCostCenterCode,
            cnpj: cnpj.trim() || null,
            unoLogin: unoLogin.trim() || null,
            unoPassword: unoPassword || null,
          }),
        });

        if (!apiResponse.ok) {
          const body = await apiResponse.text();
          throw new Error(body || `Erro HTTP ${apiResponse.status}`);
        }

        const occurrenceResponse = await apiResponse.json() as OccurrenceOpenResponse;
        setLastOccurrence(occurrenceResponse);

        const updatedDraft = buildCurrentOccurrenceDraft(
          occurrenceResponse.status === 'OC_ABERTA' ? 'ABERTA_NO_UNO' : 'ERRO_AO_ABRIR',
          occurrenceResponse.occurrenceCode,
        );
        const nextDrafts = [
          updatedDraft,
          ...occurrenceDrafts.filter((item) => item.id !== updatedDraft.id),
        ];
        persistOccurrenceDrafts(nextDrafts);
        setSelectedOccurrenceDraftId(updatedDraft.id);

        setResponse({
          status: occurrenceResponse.status,
          isHtml: false,
          responseBody: occurrenceResponse.message,
          results: [],
        });
        return;
      }

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
    setOccurrenceTitle('');
    setOccurrenceDescription('');
    setOccurrenceCategoryCode('');
    setOccurrenceTypeCode(defaultOccurrenceTypeCode);
    setOccurrenceStatusCode(defaultOccurrenceStatusCode);
    setOccurrenceCostCenterCode(defaultOccurrenceCostCenterCode);
    setSelectedOccurrenceDraftId(null);
    setLastOccurrence(null);
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

  const layoutStyle = {
    '--form-pane-width': `${formPaneWidth}px`,
    '--sidebar-width': `${sidebarCollapsed ? collapsedSidebarWidth : expandedSidebarWidth}px`,
  } as React.CSSProperties;

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

        <div ref={layoutRef} className={isResizingPane ? 'chat-layout resizing-pane' : 'chat-layout'} style={layoutStyle}>
          <aside
            className={[
              'sidebar',
              showSettings && !sidebarCollapsed ? 'settings-open' : '',
              sidebarCollapsed ? 'collapsed' : '',
            ].filter(Boolean).join(' ')}
            aria-label="Navegacao do assistente"
          >
            <div className="brand-panel">
              <img className="brand-logo" src={idSupportLogo} alt="iDSupport" />
              <img className="brand-icon" src={idIcon} alt="" aria-hidden="true" />
              <button
                className="sidebar-toggle"
                type="button"
                onClick={() => {
                  setSidebarCollapsed((value) => !value);
                  setShowSettings(false);
                }}
                title={sidebarCollapsed ? 'Expandir menu' : 'Recolher menu'}
                aria-label={sidebarCollapsed ? 'Expandir menu' : 'Recolher menu'}
              >
                {sidebarCollapsed ? <PanelLeftOpen size={18} /> : <PanelLeftClose size={18} />}
              </button>
            </div>
            <div className="mode-switch" role="tablist" aria-label="Tipo de solicitacao">
              <button
                className={requestMode === 'maintenance' ? 'mode-button active' : 'mode-button'}
                type="button"
                onClick={() => handleModeChange('maintenance')}
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
                onClick={() => handleModeChange('parts')}
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
                onClick={() => handleModeChange('exchange')}
                role="tab"
                aria-selected={requestMode === 'exchange'}
                title="Troca"
              >
                <RefreshCw size={18} />
                <span>Troca</span>
              </button>
              <button
                className={requestMode === 'occurrence' ? 'mode-button active' : 'mode-button'}
                type="button"
                onClick={() => handleModeChange('occurrence')}
                role="tab"
                aria-selected={requestMode === 'occurrence'}
                title="Ocorrências"
              >
                <Clipboard size={18} />
                <span>Ocorrências</span>
              </button>
              <button
                className={requestMode === 'invoice' ? 'mode-button active' : 'mode-button'}
                type="button"
                onClick={() => handleModeChange('invoice')}
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
                onClick={() => handleModeChange('idblock-next')}
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
            <button
              className="settings-button"
              type="button"
              onClick={() => {
                if (sidebarCollapsed) {
                  setSidebarCollapsed(false);
                  setShowSettings(true);
                  return;
                }

                setShowSettings((value) => !value);
              }}
              title="Configuracoes"
            >
              <Settings size={18} />
              <span>Configuracoes</span>
            </button>
          </aside>

          <section className="composer" aria-label="Entrada dos dados">
            <div className="message incoming flow-card">
              <div className="message-header">
                {requestMode === 'maintenance' ? <Wrench size={18} /> : requestMode === 'parts' ? <PackagePlus size={18} /> : requestMode === 'exchange' ? <RefreshCw size={18} /> : requestMode === 'occurrence' ? <Clipboard size={18} /> : requestMode === 'invoice' ? <FileText size={18} /> : <Search size={18} />}
                <span>{modeTitle}</span>
              </div>
              <div className="serial-panel">
                {requestMode === 'invoice' ? (
                  <>
                    <label htmlFor="invoice-number-input">Número</label>
                    <input
                      id="invoice-number-input"
                      value={invoiceNumber}
                      onChange={(event) => setInvoiceNumber(event.target.value)}
                      placeholder="Digite o número da NF"
                      inputMode="numeric"
                      spellCheck="false"
                      aria-label="Informe o número da nota fiscal"
                    />
                  </>
                ) : null}
                {requestMode === 'idblock-next' || requestMode === 'invoice' ? null : (
                  <>
                    <FieldLabel
                      htmlFor="cnpj-input"
                      description={requestMode === 'occurrence'
                        ? 'Insira o CNPJ do cliente da ocorrência. Se o cliente não for encontrado, a O.C será aberta no cliente padrão.'
                        : 'Insira o CNPJ da revenda que deseja abrir a RMA na ERP'}
                      onTooltipShow={showFloatingTooltip}
                      onTooltipHide={hideFloatingTooltip}
                    >
                      {requestMode === 'occurrence' ? 'CNPJ do cliente' : 'CNPJ da revenda'}
                    </FieldLabel>
                    <input
                      id="cnpj-input"
                      value={cnpj}
                      onChange={(event) => setCnpj(event.target.value)}
                      placeholder="Digite o CNPJ da revenda"
                      spellCheck="false"
                      aria-label="Informe o CNPJ da revenda"
                    />
                  </>
                )}
                {requestMode === 'occurrence' ? (
                  <>
                    <FieldLabel
                      htmlFor="occurrence-title"
                      description="Informe um título curto para identificar a ocorrência no UNO."
                      onTooltipShow={showFloatingTooltip}
                      onTooltipHide={hideFloatingTooltip}
                    >
                      Título da ocorrência
                    </FieldLabel>
                    <input
                      id="occurrence-title"
                      value={occurrenceTitle}
                      onChange={(event) => setOccurrenceTitle(event.target.value)}
                      placeholder="Digite o título da ocorrência"
                      spellCheck="false"
                      aria-label="Informe o título da ocorrência"
                    />
                    <datalist id="occurrence-category-options">
                      {occurrenceCategoryOptions.map((option) => (
                        <option key={option.code} value={option.code} label={option.name}>
                          {option.name}
                        </option>
                      ))}
                    </datalist>
                    <FieldLabel
                      htmlFor="occurrence-category"
                      description="Informe o código da categoria/equipamento da ocorrência no UNO."
                      onTooltipShow={showFloatingTooltip}
                      onTooltipHide={hideFloatingTooltip}
                    >
                      Código do equipamento
                    </FieldLabel>
                    <input
                      id="occurrence-category"
                      list="occurrence-category-options"
                      value={occurrenceCategoryCode}
                      onChange={(event) => setOccurrenceCategoryCode(event.target.value)}
                      placeholder="Selecione ou digite o código"
                      inputMode="numeric"
                      spellCheck="false"
                      aria-label="Informe o código da categoria do equipamento"
                    />
                    <div className="occurrence-select-grid">
                      <div>
                        <FieldLabel
                          htmlFor="occurrence-type"
                          description="Selecione o tipo da ocorrência que será registrado no UNO."
                          onTooltipShow={showFloatingTooltip}
                          onTooltipHide={hideFloatingTooltip}
                        >
                          Tipo da ocorrência
                        </FieldLabel>
                        <select
                          id="occurrence-type"
                          value={occurrenceTypeCode}
                          onChange={(event) => setOccurrenceTypeCode(event.target.value)}
                          aria-label="Selecione o tipo da ocorrência"
                        >
                          {occurrenceTypeOptions.map((option) => (
                            <option key={option.code} value={option.code}>{option.name}</option>
                          ))}
                        </select>
                      </div>
                      <div>
                        <FieldLabel
                          htmlFor="occurrence-status"
                          description="Selecione o status da ocorrência que será registrado no UNO."
                          onTooltipShow={showFloatingTooltip}
                          onTooltipHide={hideFloatingTooltip}
                        >
                          Status
                        </FieldLabel>
                        <select
                          id="occurrence-status"
                          value={occurrenceStatusCode}
                          onChange={(event) => setOccurrenceStatusCode(event.target.value)}
                          aria-label="Selecione o status da ocorrência"
                        >
                          {occurrenceStatusOptions.map((option) => (
                            <option key={option.code} value={option.code}>{option.name}</option>
                          ))}
                        </select>
                      </div>
                      <div>
                        <FieldLabel
                          htmlFor="occurrence-cost-center"
                          description="Selecione o centro de custo da ocorrência que será registrado no UNO."
                          onTooltipShow={showFloatingTooltip}
                          onTooltipHide={hideFloatingTooltip}
                        >
                          Centro de custo
                        </FieldLabel>
                        <select
                          id="occurrence-cost-center"
                          value={occurrenceCostCenterCode}
                          onChange={(event) => setOccurrenceCostCenterCode(event.target.value)}
                          aria-label="Selecione o centro de custo da ocorrência"
                        >
                          {occurrenceCostCenterOptions.map((option) => (
                            <option key={option.code} value={option.code}>{`${option.code} - ${option.name}`}</option>
                          ))}
                        </select>
                      </div>
                    </div>
                    <FieldLabel
                      htmlFor="occurrence-description"
                      description="Descreva a ocorrência completa para registrar no corpo da O.C."
                      onTooltipShow={showFloatingTooltip}
                      onTooltipHide={hideFloatingTooltip}
                    >
                      Corpo da ocorrência
                    </FieldLabel>
                    <textarea
                      id="occurrence-description"
                      value={occurrenceDescription}
                      onChange={(event) => setOccurrenceDescription(event.target.value)}
                      placeholder="Digite as informações do atendimento"
                      spellCheck="false"
                      aria-label="Informe o corpo da ocorrência"
                    />
                    <section className="occurrence-drafts" aria-label="Rascunhos de ocorrências">
                      <div className="occurrence-drafts-header">
                        <span>Minhas ocorrências</span>
                        <small>{unoLogin.trim() ? `Login: ${unoLogin.trim()}` : 'Sem login configurado'}</small>
                      </div>
                      {!hasOccurrenceCredentials ? (
                        <p className="occurrence-login-warning">Configure login e senha do UNO para finalizar e abrir a O.C.</p>
                      ) : null}
                      {occurrenceDrafts.length === 0 ? (
                        <p>Nenhum rascunho salvo para este usuário.</p>
                      ) : (
                        <div className="occurrence-draft-list">
                          {occurrenceDrafts.map((draft) => (
                            <article
                              key={draft.id}
                              className={selectedOccurrenceDraftId === draft.id ? 'occurrence-draft active' : 'occurrence-draft'}
                            >
                              <button type="button" onClick={() => handleSelectOccurrenceDraft(draft)}>
                                <strong>{draft.title || 'Ocorrência sem título'}</strong>
                                <span>{draft.status === 'ABERTA_NO_UNO' ? `O.C ${draft.occurrenceCode}` : draft.status === 'ERRO_AO_ABRIR' ? 'Erro ao abrir' : 'Rascunho'}</span>
                              </button>
                              <button type="button" className="draft-delete-button" onClick={() => handleDeleteOccurrenceDraft(draft.id)} title="Remover rascunho">
                                Remover
                              </button>
                            </article>
                          ))}
                        </div>
                      )}
                    </section>
                  </>
                ) : null}
                {requestMode === 'invoice' || requestMode === 'occurrence' ? null : (
                  <FieldLabel
                    htmlFor="serial-input"
                    description={requestMode === 'idblock-next'
                      ? 'Insira o serial do IDFace pertencente a catraca IDBlock Next'
                      : 'Insira o número de série dos equipamentos, caso tenha mais de um serial separado em linhas diferentes é gerado uma OS para cada, porém com a mesma descrição.'}
                    onTooltipShow={showFloatingTooltip}
                    onTooltipHide={hideFloatingTooltip}
                  >
                    {requestMode === 'idblock-next' ? 'Serial do IDFace' : 'Número de série dos equipamentos'}
                  </FieldLabel>
                )}
                {requestMode === 'invoice' || requestMode === 'occurrence' ? null : requestMode === 'idblock-next' ? (
                  <input
                    id="serial-input"
                    value={serial}
                    onChange={(event) => setSerial(event.target.value)}
                    placeholder="Digite o serial do IDFace"
                    spellCheck="false"
                    aria-label="Informe o numero de serie do IDFace"
                  />
                ) : (
                  <textarea
                    id="serial-input"
                    value={serial}
                    onChange={(event) => setSerial(event.target.value)}
                    placeholder="Digite um número de série por linha"
                    spellCheck="false"
                    aria-label="Informe um ou mais numeros de serie"
                  />
                )}
                {isServiceOrderMode ? (
                  <>
                    <FieldLabel
                      htmlFor="service-order-defect"
                      description="Insira o defeito relatado pelo revendedor/instalador"
                      onTooltipShow={showFloatingTooltip}
                      onTooltipHide={hideFloatingTooltip}
                    >
                      Defeito relatado
                    </FieldLabel>
                    <textarea
                      id="service-order-defect"
                      value={serviceOrderDefect}
                      onChange={(event) => setServiceOrderDefect(event.target.value)}
                      placeholder="Digite o defeito informado pelo cliente"
                      spellCheck="false"
                      aria-label="Informe o defeito relatado"
                    />
                    {requestMode === 'maintenance' || requestMode === 'exchange' ? (
                      <>
                        <FieldLabel
                          htmlFor="uno-observations"
                          description="Insira uma observação para a abertura da O.S (caso haja)"
                          onTooltipShow={showFloatingTooltip}
                          onTooltipHide={hideFloatingTooltip}
                        >
                          Observações
                        </FieldLabel>
                        <textarea
                          id="uno-observations"
                          value={unoObservations}
                          onChange={(event) => setUnoObservations(event.target.value)}
                          placeholder="Digite observações para a O.S"
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
                      <InfoTooltip description={warrantyHelpText} onShow={showFloatingTooltip} onHide={hideFloatingTooltip} />
                    </label>
                  </>
                ) : null}
                {requestMode === 'parts' ? (
                  <>
                    <FieldLabel
                      htmlFor="part-input"
                      description="Insira o código da peça a ser enviada"
                      onTooltipShow={showFloatingTooltip}
                      onTooltipHide={hideFloatingTooltip}
                    >
                      Peca a ser enviada
                    </FieldLabel>
                    <input
                      id="part-input"
                      value={partToSend}
                      onChange={(event) => setPartToSend(event.target.value)}
                      placeholder="Digite a peça"
                      spellCheck="false"
                      aria-label="Informe a peca a ser enviada"
                    />
                  </>
                ) : null}
              </div>
            </div>
            <div className={requestMode === 'occurrence' ? 'actions occurrence-actions' : 'actions'}>
              <button className="secondary-button" type="button" onClick={handleReset} title="Limpar">
                <RotateCcw size={18} />
                <span>Limpar</span>
              </button>
              {requestMode === 'occurrence' ? (
                <button className="secondary-button" type="button" onClick={handleSaveOccurrenceDraft} disabled={!canSaveOccurrenceDraft} title="Salvar rascunho">
                  <Clipboard size={18} />
                  <span>Salvar rascunho</span>
                </button>
              ) : null}
              <button
                className="primary-button"
                type="button"
                onClick={handleGenerate}
                disabled={loading || !canSubmit}
                title={requestMode === 'occurrence' && !hasOccurrenceCredentials ? 'Configure login e senha do UNO para abrir a O.C' : 'Executar'}
              >
                {requestMode === 'idblock-next' ? <Search size={18} /> : requestMode === 'invoice' ? <FileText size={18} /> : requestMode === 'occurrence' ? <Clipboard size={18} /> : <Send size={18} />}
                <span>
                  {loading
                    ? requestMode === 'idblock-next' ? 'Consultando SPOC' : requestMode === 'invoice' ? 'Buscando NF' : requestMode === 'occurrence' ? 'Abrindo O.C' : 'Consultando UNO'
                    : requestMode === 'maintenance'
                      ? 'Gerar manutenção'
                      : requestMode === 'exchange'
                        ? 'Gerar troca'
                        : requestMode === 'parts'
                          ? 'Abrir O.S e gerar template'
                          : requestMode === 'invoice'
                            ? 'Buscar NF'
                            : requestMode === 'occurrence'
                              ? 'Finalizar e abrir O.C'
                          : 'Consultar SPOC'}
                </span>
              </button>
            </div>
          </section>

          <div
            className="pane-resizer"
            role="separator"
            aria-label="Ajustar largura entre formulario e resultado"
            aria-orientation="vertical"
            aria-valuemin={minFormPaneWidth}
            aria-valuemax={getMaxFormPaneWidth(sidebarCollapsed ? collapsedSidebarWidth : expandedSidebarWidth)}
            aria-valuenow={Math.round(formPaneWidth)}
            tabIndex={0}
            onPointerDown={handlePaneResizeStart}
            onKeyDown={handlePaneResizeKeyDown}
            onDoubleClick={() => updateFormPaneWidth(defaultFormPaneWidth)}
            title="Arraste para ajustar. Duplo clique para restaurar."
          >
            <span />
          </div>

          <section className="result" aria-label={resultAriaLabel}>
            <div className="message outgoing result-card">
              <div className="message-header">
                {requestMode === 'invoice' ? <FileText size={18} /> : requestMode === 'idblock-next' ? <Search size={18} /> : <Clipboard size={18} />}
                <span>{resultTitle}</span>
              </div>
              {isBusy ? (
                <div className="panel-loading-state" role="status" aria-live="polite">
                  <div className="panel-loading-icon">
                    <RefreshCw size={28} />
                  </div>
                  <strong>{busyTitle}</strong>
                  <span>{busyDescription}</span>
                </div>
              ) : null}
              {!isBusy && shouldShowExtractedGrid ? (
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
              {!isBusy && requestMode === 'idblock-next' && spocResolution?.nextSerial ? (
                <div className="next-serial-box">
                  <span>Serial da IDBlock Next encontrado no SPOC</span>
                  <strong>{spocResolution.nextSerial}</strong>
                </div>
              ) : null}
              {!isBusy && shouldShowInvoicePreview ? (
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
              {!isBusy && requestMode === 'occurrence' && lastOccurrence ? (
                <div className="occurrence-result">
                  <span>{lastOccurrence.status === 'OC_ABERTA' ? 'Ocorrência aberta no UNO' : 'Retorno da ocorrência'}</span>
                  <strong>{lastOccurrence.occurrenceCode ? `O.C ${lastOccurrence.occurrenceCode}` : lastOccurrence.message}</strong>
                  <dl>
                    <div>
                      <dt>Título</dt>
                      <dd>{lastOccurrence.title || occurrenceTitle || '-'}</dd>
                    </div>
                    <div>
                      <dt>Cliente</dt>
                      <dd>{lastOccurrence.customerCode ? `${lastOccurrence.customerCode}${lastOccurrence.customerName ? ` - ${lastOccurrence.customerName}` : ''}` : cnpj || 'Cliente padrão se CNPJ não for encontrado'}</dd>
                    </div>
                    <div>
                      <dt>Categoria</dt>
                      <dd>{lastOccurrence.categoryCode || occurrenceCategoryCode || '-'}</dd>
                    </div>
                  </dl>
                </div>
              ) : null}
              {!isBusy && response.isHtml ? (
                <div className="html-preview">
                  <div className="email-preview-surface" dangerouslySetInnerHTML={{ __html: response.responseBody }} />
                </div>
              ) : !isBusy && isEmptyResult ? (
                <div className="empty-state">
                  <div className="empty-state-icon">
                    {requestMode === 'invoice' ? <FileText size={28} /> : requestMode === 'idblock-next' ? <Search size={28} /> : <Clipboard size={28} />}
                  </div>
                  <strong>{emptyStateTitle}</strong>
                  <span>{emptyStateDescription}</span>
                </div>
              ) : isBusy || shouldShowInvoicePreview ? null : (
                <div className={`response-card response-${responseTone}`}>
                  <div className="response-card-icon">
                    {responseTone === 'success' ? <CheckCircle2 size={22} /> : <ShieldAlert size={22} />}
                  </div>
                  <div>
                    <strong>{responseCardTitle}</strong>
                    <p>{responseCardMessage}</p>
                  </div>
                </div>
              )}
              {!isBusy && canOpenServiceOrder ? (
                <div className="service-order-prompt">
                  <span>{requestMode === 'exchange' ? 'Deseja abrir a O.S de troca no UNO?' : 'Deseja abrir a O.S de manutenção no UNO?'}</span>
                  <button type="button" onClick={requestMode === 'exchange' ? handleOpenExchangeServiceOrder : handleOpenServiceOrder} disabled={openingServiceOrder}>
                    {requestMode === 'exchange' ? <RefreshCw size={18} /> : <Wrench size={18} />}
                    <span>{openingServiceOrder ? 'Abrindo O.S' : 'Abrir O.S'}</span>
                  </button>
                  {serviceOrderStatus ? <p>{serviceOrderStatus}</p> : null}
                </div>
              ) : null}
              {!isBusy && canCopyResult ? (
                <button className="copy-button" type="button" onClick={handleCopy} title={requestMode === 'idblock-next' ? 'Copiar serial' : 'Copiar resposta'}>
                  <Clipboard size={18} />
                  <span>{copied ? 'Copiado' : requestMode === 'idblock-next' ? 'Copiar serial' : 'Copiar resposta'}</span>
                </button>
              ) : null}
            </div>
          </section>
        </div>
      </section>
      {activeTooltip ? createPortal(
        <div
          className={`floating-field-tooltip floating-field-tooltip-${activeTooltip.placement}${theme === 'dark' ? ' dark' : ''}`}
          role="tooltip"
          style={{
            left: activeTooltip.left,
            top: activeTooltip.top,
            maxWidth: activeTooltip.maxWidth,
          }}
        >
          {activeTooltip.description}
        </div>,
        document.body,
      ) : null}
    </main>
  );
}

ReactDOM.createRoot(document.getElementById('root')!).render(<App />);
