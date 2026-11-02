import { Loader2 } from "lucide-react";
import { useState, type FormEvent } from "react";
import { api, ApiError } from "../api";
import { useShop } from "../context";
import { linkProps, navigate } from "../router";

export function SignIn({ mode, next }: { mode: "signin" | "register"; next?: string }) {
  const { setSession } = useShop();
  const register = mode === "register";
  const [form, setForm] = useState({ email: "", displayName: "", password: "" });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const target = next?.startsWith("/") && !next.startsWith("//") ? next : "/";
  const set = (key: keyof typeof form) => (e: { target: { value: string } }) => setForm((f) => ({ ...f, [key]: e.target.value }));

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      setSession(register ? await api.register(form.email, form.displayName, form.password) : await api.login(form.email, form.password));
      navigate(target, true);
    } catch (err) {
      setError(err as ApiError);
    } finally {
      setBusy(false);
    }
  }

  const query = next ? `?next=${encodeURIComponent(next)}` : "";
  return (
    <div className="container narrow">
      <form className="card section auth" onSubmit={submit} noValidate>
        <h1>{register ? "Create an account" : "Sign in"}</h1>
        <label className="field">Email<input className="input" type="email" value={form.email} onChange={set("email")} autoComplete="email" />{error?.fields.email && <span className="field-error">{error.fields.email[0]}</span>}</label>
        {register && (
          <label className="field">Your name<input className="input" value={form.displayName} onChange={set("displayName")} autoComplete="name" />{error?.fields.displayName && <span className="field-error">{error.fields.displayName[0]}</span>}</label>
        )}
        <label className="field">Password<input className="input" type="password" value={form.password} onChange={set("password")} autoComplete={register ? "new-password" : "current-password"} />{register && <span className="hint">At least 10 characters.</span>}{error?.fields.password && <span className="field-error">{error.fields.password[0]}</span>}</label>
        {error && Object.keys(error.fields).length === 0 && <p className="form-error" role="alert">{error.message}</p>}
        <button className="button primary wide" disabled={busy}>{busy && <Loader2 size={16} className="spin" />}{register ? "Create account" : "Sign in"}</button>
        <p className="small muted" style={{ textAlign: "center" }}>
          {register ? "Already have an account? " : "New here? "}
          <a {...linkProps(`/${register ? "signin" : "register"}${query}`)}>{register ? "Sign in" : "Create an account"}</a>
        </p>
      </form>
    </div>
  );
}
