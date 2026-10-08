import { GoogleOAuthProvider } from '@react-oauth/google'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'

const googleClientId = import.meta.env.VITE_GOOGLE_CLIENT_ID

export function AppProviders({ children }: { children: ReactNode }) {
  // Cache công khai tối đa 30 giây (spec §4.4)
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        retry: 1,
        refetchOnWindowFocus: false,
      },
    },
  })

  return (
    <QueryClientProvider client={queryClient}>
      {googleClientId ? (
        <GoogleOAuthProvider clientId={googleClientId}>{children}</GoogleOAuthProvider>
      ) : (
        children
      )}
    </QueryClientProvider>
  )
}
