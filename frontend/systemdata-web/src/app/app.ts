import { CommonModule } from '@angular/common';
import { Component, ElementRef, OnDestroy, OnInit, ViewChild, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CommerceApiService, QuarantineRow } from './services/commerce-api.service';


@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App implements OnInit, OnDestroy {
  @ViewChild('csvFile') private csvFileInput?: ElementRef<HTMLInputElement>;
  private readonly api = inject(CommerceApiService);
  private toastTimeout?: ReturnType<typeof setTimeout>;
  protected readonly successToast = signal('');
  protected readonly page = signal<'upload' | 'process' | 'quarantine'>('upload');
  protected readonly file = signal<File | null>(null);
  protected readonly headers = signal<string[]>([]);
  protected readonly previewRows = signal<string[][]>([]);
  protected readonly totalRows = signal(0);
  protected readonly quarantine = signal<QuarantineRow[]>([]);
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly error = signal('');
  protected processDate = (() => { const date = new Date(); return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`; })();

  ngOnInit(): void { this.loadQuarantine(); }
  ngOnDestroy(): void { if (this.toastTimeout) clearTimeout(this.toastTimeout); }

  protected closeToast(): void {
    if (this.toastTimeout) clearTimeout(this.toastTimeout);
    this.successToast.set('');
  }

  private showToast(message: string): void {
    this.closeToast();
    this.successToast.set(message);
    this.toastTimeout = setTimeout(() => this.successToast.set(''), 5000);
  }

  protected navigate(page: 'upload' | 'process' | 'quarantine'): void {
    this.page.set(page);
    this.closeToast();
    this.message.set('');
    this.error.set('');
    if (page === 'quarantine') this.loadQuarantine();
  }

  protected async onFileChange(event: Event): Promise<void> {
    this.message.set('');
    this.closeToast();
    this.error.set('');
    this.headers.set([]);
    this.previewRows.set([]);
    this.totalRows.set(0);
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    if (!file) { this.file.set(null); return; }
    if (!/^commerce_\d{8}\.csv$/i.test(file.name)) { this.file.set(null); this.error.set('El archivo debe llamarse commerce_DDMMYYYY.csv.'); return; }
    if (file.size === 0) { this.file.set(null); this.error.set('El archivo está vacío.'); return; }
    if (file.size > 25 * 1024 * 1024) { this.file.set(null); this.error.set('El archivo supera el límite de 25 MB.'); return; }
    this.file.set(file);
    try {
      const rows = this.parseCsv(await file.text());
      if (rows.length < 2) throw new Error('El archivo no contiene filas de datos.');
      const names = rows[0].map((value) => value.trim().replace(/^\uFEFF/, ''));
      const required = ['pc_processdate', 'pc_nomcomred', 'pc_numdoc'];
      const missing = required.filter((name) => !names.some((header) => header.toLowerCase() === name));
      if (missing.length) throw new Error(`Faltan columnas requeridas: ${missing.join(', ')}.`);
      if (names.length !== 3 || new Set(names.map((value) => value.toLowerCase())).size !== names.length)
        throw new Error('El CSV de esta versión debe contener exactamente las tres columnas requeridas, sin duplicados.');
      this.headers.set(names);
      const records = rows.slice(1).filter((row) => row.some((value) => value.trim()));
      if (records.some((row) => row.length !== names.length))
        throw new Error('Una o más filas tienen una cantidad de columnas distinta del encabezado.');
      if (records.some((row) => row.some((value) => value.length > 4000)))
        throw new Error('Uno o más campos exceden los 4000 caracteres permitidos.');
      this.totalRows.set(records.length);
      this.previewRows.set(records.slice(0, 8));
    } catch (cause) {
      this.file.set(null);
      this.error.set(cause instanceof Error ? cause.message : 'No se pudo leer el CSV.');
      input.value = '';
    }
  }

  protected upload(): void {
    // Evita que dos clics rápidos envíen el mismo archivo simultáneamente.
    if (this.busy()) return;

    const file = this.file();
    if (!file) { this.error.set('Selecciona un archivo CSV para continuar.'); return; }

    this.busy.set(true);
    this.message.set('');
    this.error.set('');

    this.api.upload(file).subscribe({
      next: (result) => {
        // Solo se limpia el formulario si la API confirmó la inserción.
        this.file.set(null);
        this.headers.set([]);
        this.previewRows.set([]);
        this.totalRows.set(0);
        if (this.csvFileInput) this.csvFileInput.nativeElement.value = '';

        this.showToast(`Archivo cargado correctamente. ${result.inserted} filas registradas.`);
        this.busy.set(false);
      },
      error: (response) => {
        // Conserva el CSV y la vista previa para facilitar el reintento.
        this.error.set(response.error?.message ?? 'No se pudo cargar el archivo.');
        this.busy.set(false);
      },
    });
  }

  protected process(): void {
    if (this.busy()) return;
    if (!this.processDate) { this.error.set('Selecciona la fecha de proceso.'); return; }
    this.busy.set(true);
    this.message.set('');
    this.error.set('');
    this.api.process(this.processDate).subscribe({
      next: (result) => {
        this.message.set(result.message);
        this.busy.set(false);
        this.loadQuarantine();
      },
      error: (response) => { this.error.set(response.error?.message ?? 'No se pudo procesar la fecha.'); this.busy.set(false); },
    });
  }

  protected loadQuarantine(): void {
    this.api.quarantine().subscribe({
      next: (rows) => this.quarantine.set(rows),
      error: () => { this.quarantine.set([]); if (this.page() === 'quarantine') this.error.set('No se pudo consultar la cuarentena. Comprueba la API y la conexión SQL Server.'); },
    });
  }

  protected fileSizeLabel(): string {
    const file = this.file();
    return file ? `${Math.max(1, Math.round(file.size / 1024))} KB · ${this.totalRows()} registros` : '';
  }

  private parseCsv(text: string): string[][] {
    const records: string[][] = [];
    let row: string[] = [];
    let cell = '';
    let quoted = false;
    for (let i = 0; i < text.length; i++) {
      const char = text[i];
      if (quoted) {
        if (char === '"' && text[i + 1] === '"') { cell += '"'; i++; }
        else if (char === '"') quoted = false;
        else cell += char;
      } else if (char === '"' && cell.length === 0) quoted = true;
      else if (char === ',') { row.push(cell); cell = ''; }
      else if (char === '\r' || char === '\n') {
        if (char === '\r' && text[i + 1] === '\n') i++;
        row.push(cell); records.push(row); row = []; cell = '';
      } else cell += char;
    }
    if (quoted) throw new Error('Hay un campo entre comillas que no se cerró.');
    if (cell.length || row.length) { row.push(cell); records.push(row); }
    return records;
  }
}
