# SystemData · Frontend

Interfaz web para importar archivos CSV, validar registros comerciales y consultar datos en cuarentena. Construida con Angular 20.

## Requisitos

- Node.js 20.19+ o 22.12+
- npm
- API disponible en `http://localhost:5111`

## Ejecutar en desarrollo

```powershell
npm install
npm start
```

Abre `http://localhost:4200`. La dirección de la API se configura en `src/environments/environment.ts`.

## Compilar

```powershell
npm run build
```

La versión optimizada se genera en `dist/`.
