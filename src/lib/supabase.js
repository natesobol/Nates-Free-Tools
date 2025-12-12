import { createClient } from '@supabase/supabase-js';

const supabaseUrl = process.env.SUPABASE_URL || 'https://ggnltydbbpeadrfiqlhw.supabase.co';
const supabaseAnonKey =
  process.env.SUPABASE_ANON_KEY ||
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Imdnbmx0eWRiYnBlYWRyZmlxbGh3Iiwicm9sZSI6ImFub24iLCJpYXQiOjE3NjU1MTQ4NDYsImV4cCI6MjA4MTA5MDg0Nn0.uGAPRMIAihRiY9JTC7QJbQUrw4UjBhn2u833OcjEXEM';

export function createSupabaseClient(accessToken, refreshToken) {
  if (!supabaseUrl || !supabaseAnonKey) {
    throw new Error('Missing SUPABASE_URL or SUPABASE_ANON_KEY');
  }

  const client = createClient(supabaseUrl, supabaseAnonKey, { auth: { persistSession: false } });

  if (accessToken && refreshToken) {
    client.auth.setSession({ access_token: accessToken, refresh_token: refreshToken }).catch(() => {});
  }

  return client;
}
