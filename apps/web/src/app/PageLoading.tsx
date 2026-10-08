import { Loader2 } from "lucide-react";

/** Fallback Suspense cho các route lazy-load (spec §8.5: /gv, /admin tách bundle). */
export function PageLoading() {
  return (
    <div
      className="grid min-h-[50dvh] place-items-center"
      role="status"
      aria-label="Đang tải"
    >
      <Loader2 className="size-6 animate-spin text-violet" aria-hidden />
    </div>
  );
}
