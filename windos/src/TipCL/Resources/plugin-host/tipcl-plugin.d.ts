// Typings for TipCL Node plugins (host side) and their web views (client side). See docs/plugins/DESIGN.md.
// Usage: copy next to your sources, or `/// <reference path="tipcl-plugin.d.ts" />`.

// ─────────────────────────── shared ───────────────────────────

export type Dispose = () => void;

export type ContentPart =
  | { type: 'text'; text: string }
  | { type: 'image'; attachment: Attachment | { id: string } | string; mediaType?: string }
  | { type: 'image'; data: Uint8Array | ArrayBuffer | string; mediaType?: string };

export interface Attachment { id: string; mediaType: string; name?: string }

/** AI chat event (DESIGN §5). `time` mirrors `at` for DSH compatibility. */
export interface ChatEvent {
  seq: number;
  type: 'turn/start' | 'user/message' | 'assistant/delta' | 'assistant/reasoning' | 'assistant/message' | 'tool/start' | 'tool/result' | 'turn/end' | string;
  data?: any;
  at?: number;
  time?: number;
}

export interface LlmChunk {
  type: 'text-delta' | 'reasoning-delta' | 'tool-call' | 'finish' | string;
  text?: string;
  reason?: { kind: 'stop' | 'length' | 'error' | 'aborted' | string };
  usage?: any;
  [k: string]: any;
}

export interface LlmRequest {
  provider: string;
  model: string;
  system?: string;
  messages: { role: 'user' | 'assistant' | 'system' | string; content: string | ContentPart[]; [k: string]: any }[];
  maxTokens?: number;
  temperature?: number;
  reasoningEffort?: 'off' | 'low' | 'medium' | 'high' | string;
  tools?: any[];
  signal?: AbortSignal;
  [k: string]: any;
}

export interface AgentPreset {
  id: string;
  name: string;
  description?: string;
  order?: number;
  systemPrompt?: string;
  systemPromptFile?: string;
  replaceSystemPrompt?: boolean;
  includeRuntimeContext?: boolean;
  tools?: { include?: string[]; builtin?: boolean };
  maxRounds?: number;
  view?: string;
}

export interface UiSpecBase { id: string; label?: string; icon?: string; order?: number; view?: string }
export interface PageSpec extends UiSpecBase { view: string; group?: 'top' | 'more' }
export interface AiViewSpec extends UiSpecBase { view: string; presets?: string[] }
export interface DashCardSpec extends UiSpecBase { view: string; defaultW?: number; defaultH?: number }
export interface OverlaySpec { id: string; view: string; width: number; height: number; anchor?: 'bottom-right' | 'bottom-left' | 'top-right' | 'top-left' | string; margin?: number; clickThrough?: boolean; visible?: boolean }
export interface ActionSpec { id: string; label: string; where: 'instance' | 'launch' | 'tray' | 'ai.input'; icon?: string }

// ─────────────────────────── host side ───────────────────────────

/** DSH-style parameter map, or a JSON Schema object. */
export type ToolParameters =
  | Record<string, { type: 'string' | 'integer' | 'number' | 'boolean' | 'array' | 'object' | 'json'; required?: boolean; description?: string; enum?: any[]; items?: any; properties?: any }>
  | { type: 'object'; properties?: Record<string, any>; required?: string[] };

export interface ToolExec {
  callId: string;
  chatId: string | null;
  /** DSH compat: `agent.id` is the chat id */
  agent: { id: string | null };
  /** aborted when the chat is stopped (TipCL `tool.cancel`) or the plugin unloads */
  signal: AbortSignal;
  /** text of the assistant message that contains this tool call */
  messageText: string;
}

export interface ToolDefinition<A = any, V = any> {
  name: string;
  description?: string;
  parameters?: ToolParameters;
  /** default 30 min on the TipCL side */
  timeoutMs?: number;
  label?: string;
  /** turn the returned value into content parts (DSH `output.render`) */
  output?: { schema?: any; render?: (args: A, value: V) => ContentPart[] | Promise<ContentPart[]> };
  /** string → text; `{content, isError}` → used as is; anything else → JSON text (unless output.render) */
  execute(args: A, exec: ToolExec): V | Promise<V>;
}

export interface ConfigCell<T = any> { get(): T; set(value: T): void }
export type ConfigCells = Record<string, ConfigCell>;

export interface SessionView {
  id: string;
  readonly seq: number;
  readonly running: boolean;
  /** synchronous: events with seq > from */
  snapshotEvents(from?: number): ChatEvent[];
  send(text: string, opts?: { mode?: 'auto' | 'queue' | 'steer'; images?: any[] }): Promise<{ ok: boolean; mode?: string; error?: string }>;
  cancel(): Promise<any>;
  open(opts?: { view?: string }): Promise<any>;
  ready(): Promise<void>;
}

export interface LlmService {
  /** synchronous (cached) */
  listProviders(): { id: string; name: string }[];
  listModels(provider: string): Promise<{ id: string; name?: string }[]>;
  stream(req: LlmRequest): AsyncIterable<LlmChunk>;
  complete(req: LlmRequest): Promise<string>;
}
export interface CredentialsService {
  resolve(name: string): Promise<{ value: string } | undefined>;
  set(name: string, value: string | null): Promise<void>;
  status(): Promise<Record<string, { configured: boolean }>>;
  onChange(fn: (data: { name: string; configured: boolean }) => void): Dispose;
}
export interface SessionsService {
  get(chatId: string): SessionView | undefined;
  list(): Promise<{ chatId: string; title: string; preset?: string; updatedAt: any; running: boolean }[]>;
  create(opts?: { preset?: string; title?: string; view?: string; prompt?: string; open?: boolean }): Promise<{ chatId: string }>;
  send(chatId: string, text: string, opts?: { mode?: 'auto' | 'queue' | 'steer'; images?: any[] }): Promise<{ ok: boolean; mode?: string; error?: string }>;
  cancel(chatId: string): Promise<any>;
  open(chatId: string, opts?: { view?: string }): Promise<any>;
  state(chatId: string): Promise<{ running: boolean; seq: number }>;
  events(chatId: string, from?: number): Promise<{ seq: number; events: ChatEvent[] }>;
  subscribe(chatId: string, fn: (e: ChatEvent) => void): Dispose;
}

export interface RouteSpec {
  /** may contain `*` wildcards; keep it under /api/<plugin-id>/ */
  path: string;
  methods?: string[];
  /** 'buffered' (default) or 'stream' (request.body is a live ReadableStream) */
  requestBody?: 'buffered' | 'stream';
  fetch(request: Request, extra: { plugin: string; signal: AbortSignal }): Response | Promise<Response>;
}

export interface PluginContext {
  readonly id: string;
  readonly manifest: any;
  readonly paths: { plugin: string; data: string; cache: string; logs: string };
  effect(setup: () => void | Dispose | { dispose(): void }, label?: string): Dispose;
  /** Ask TipCL to unload and reload this plugin (`plugin.reload`). */
  reload(): Promise<void>;
  logger: { debug(...a: any[]): void; info(...a: any[]): void; warn(...a: any[]): void; error(...a: any[]): void };
  get(name: 'llm'): LlmService;
  get(name: 'credentials'): CredentialsService;
  get(name: 'sessions'): SessionsService;
  get<T = any>(name: string): T | undefined;
  provide(name: string, service: any): Dispose;
  inject(names: string[], callback: (ctx: PluginContext) => void): Dispose;
  tools: { register(tool: ToolDefinition): Dispose };
  presets: { register(preset: AgentPreset): Dispose };
  connection: { fetch: { register(spec: RouteSpec): Dispose } };
  attachments: { saveImages(images: { data: Uint8Array | ArrayBuffer | string; mediaType?: string; name?: string }[]): Promise<Attachment[]> };
  launcher: { call<T = any>(method: string, kwargs?: Record<string, any> | any[]): Promise<T>; on(topic: string, fn: (data: any, topic: string) => void): Dispose };
  events: { on(topic: string, fn: (data: any, topic: string) => void): Dispose };
  hooks: {
    register(hook: 'launch.before', fn: (data: { instance: string; version: string; javaPath: string; jvmArgs: string[]; gameArgs: string[]; env: Record<string, string>; account: { name: string; type: string } }) => any, opts?: { priority?: number }): Dispose;
    register(hook: string, fn: (data: any) => any, opts?: { priority?: number }): Dispose;
  };
  config: { get(): Record<string, any>; set(key: string, value: any): Promise<void>; onChange(fn: (key: string, value: any, all: Record<string, any>) => void): Dispose };
  ui: {
    page(spec: PageSpec): Dispose;
    aiView(spec: AiViewSpec): Dispose;
    dashCard(spec: DashCardSpec): Dispose;
    overlay(spec: OverlaySpec): Dispose;
    settings(spec: { view: string }): Dispose;
    action(spec: ActionSpec, handler: (context: any) => void): Dispose;
    toast(text: string, kind?: 'info' | 'success' | 'warn' | 'error'): Promise<void>;
    confirm(title: string, text: string, opts?: { ok?: string; cancel?: string }): Promise<boolean>;
    prompt(title: string, text: string, opts?: { placeholder?: string; default?: string }): Promise<string | null>;
    openPage(page: string): Promise<void>;
    openExternal(url: string): Promise<void>;
    overlayUpdate(id: string, patch: { visible?: boolean; width?: number; height?: number; anchor?: string; clickThrough?: boolean }): Promise<void>;
    onEvent(fn: (e: { kind: string; id: string; event: string; data: any }) => void): Dispose;
    viewUrl(view: string, kind?: string, id?: string): string;
  };
  /** DSH compat: unknown properties resolve like ctx.get(name) */
  [service: string]: any;
}

/** Shape of a `main` entry module. */
export interface PluginModule {
  name?: string;
  /** services required before apply (built-ins or provided by other plugins) */
  inject?: string[] | { required?: string[]; optional?: string[] };
  apply(ctx: PluginContext, config: ConfigCells): void | Promise<void>;
}

// ─────────────────────────── client side (browser) ───────────────────────────

export interface Tipcl {
  plugin: { id: string; version: string; dir: string; name?: string };
  surface: { kind: 'page' | 'aiView' | 'dashCard' | 'overlay' | 'settings' | string; id: string };
  props: Record<string, any>;
  theme: { mode: 'light' | 'dark'; colors: { bg: string; fg: string; card: string; border: string; accent: string; muted: string; danger: string; [k: string]: string }; font: string; onChange(fn: (t: Tipcl['theme']) => void): Dispose };
  call<T = any>(method: string, params?: Record<string, any>): Promise<T>;
  events: { on(topic: string, fn: (data: any, topic: string) => void): Dispose };
  ai: {
    createChat(opts?: { preset?: string; view?: string; prompt?: string; open?: boolean; title?: string }): Promise<{ chatId: string }>;
    send(chatId: string, text: string, opts?: { mode?: 'auto' | 'queue' | 'steer'; images?: any[] }): Promise<{ ok: boolean; mode?: string; error?: string }>;
    cancel(chatId: string): Promise<any>;
    open(chatId: string, opts?: { view?: string }): Promise<any>;
    list(): Promise<any[]>;
    events(chatId: string, from?: number): Promise<{ seq: number; events: ChatEvent[] }>;
    subscribe(chatId: string, fn: (e: ChatEvent) => void, opts?: { from?: number }): Dispose;
    state(chatId: string): Promise<{ running: boolean; seq: number }>;
  };
  llm: { providers(): Promise<{ id: string; name: string }[]>; models(provider: string): Promise<{ id: string; name?: string }[]> };
  credentials: { status(): Promise<Record<string, { configured: boolean }>>; set(name: string, value: string | null): Promise<void>; onChange(fn: (d: { name: string; configured: boolean }) => void): Dispose };
  config: { get(): Promise<Record<string, any>>; set(key: string, value: any): Promise<void>; onChange(fn: (key: string, value: any) => void): Dispose };
  nav: { open(page: string): Promise<void> };
  ui: { toast(text: string, kind?: string): Promise<void>; confirm(title: string, text: string, opts?: any): Promise<boolean>; prompt(title: string, text: string, opts?: any): Promise<string | null>; openExternal(url: string): Promise<void> };
  overlay: { update(patch: { visible?: boolean; width?: number; height?: number; anchor?: string; clickThrough?: boolean }): Promise<void> };
  launcher: { call<T = any>(method: string, kwargs?: Record<string, any> | any[]): Promise<T> };
  asset(path: string): string;
}

export interface ClientUi {
  tipcl: Tipcl;
  view(name: string, mount: (el: HTMLElement, props: Record<string, any> & { chatId?: string; kind: string; id: string }, tipcl: Tipcl) => void | Dispose | Promise<void | Dispose>): Dispose;
  css(text: string): Dispose;
}

/** Shape of the `client` module. */
export interface ClientModule { apply(ui: ClientUi, tipcl: Tipcl): void | Promise<void> }

// `import { defineTool } from 'tipcl:tools'` (also `@deepseek-ai/dsh-tools`) — mapped by the host.
declare module 'tipcl:tools' {
  export function defineTool<T extends import('./tipcl-plugin').ToolDefinition>(tool: T): T;
  export function definePreset<T>(preset: T): T;
  export function definePlugin<T>(plugin: T): T;
  export function text(value: string): { type: 'text'; text: string }[];
  export function image(source: any, mediaType?: string): any;
}
