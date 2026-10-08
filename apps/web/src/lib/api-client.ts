import createClient from 'openapi-fetch'
import type { paths } from './api-types'

/**
 * Client API duy nhất. Mọi request ghi mang header X-Requested-With: hoclieu (CSRF, spec §3.4).
 * Cookie phiên đi qua credentials: include (same-origin qua proxy Caddy/Vite).
 */
export const api = createClient<paths>({
  baseUrl: '/',
  credentials: 'include',
  headers: {
    'X-Requested-With': 'hoclieu',
  },
})

export type { paths }
