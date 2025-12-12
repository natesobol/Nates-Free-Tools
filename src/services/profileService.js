import { createSupabaseClient } from '../lib/supabase.js';

export async function fetchProfileById(userId, supabaseClient) {
  const supabase = supabaseClient || createSupabaseClient();
  const { data, error } = await supabase
    .from('profiles')
    .select('user_id, full_name, role, marketing_opt_in, created_at, updated_at')
    .eq('user_id', userId)
    .maybeSingle();

  if (error) {
    throw error;
  }

  return data;
}

export async function ensureProfile({ userId, fullName, marketingOptIn = false, role = 'user' }, supabaseClient) {
  const supabase = supabaseClient || createSupabaseClient();

  const existing = await fetchProfileById(userId, supabase).catch(() => null);
  if (existing) return existing;

  const { data, error } = await supabase
    .from('profiles')
    .insert({ user_id: userId, full_name: fullName, marketing_opt_in: marketingOptIn, role })
    .select('user_id, full_name, role, marketing_opt_in, created_at, updated_at')
    .single();

  if (error) throw error;
  return data;
}

export async function updateProfile(userId, updates, supabaseClient) {
  const supabase = supabaseClient || createSupabaseClient();
  const payload = {
    full_name: updates.fullName,
    marketing_opt_in: updates.marketingOptIn
  };

  const { data, error } = await supabase
    .from('profiles')
    .update(payload)
    .eq('user_id', userId)
    .select('user_id, full_name, role, marketing_opt_in, created_at, updated_at')
    .single();

  if (error) throw error;
  return data;
}

export async function listProfiles(supabaseClient) {
  const supabase = supabaseClient || createSupabaseClient();
  const { data, error } = await supabase
    .from('profiles')
    .select('user_id, full_name, role, marketing_opt_in, created_at, updated_at')
    .order('created_at', { ascending: false });

  if (error) throw error;
  return data;
}

export async function fetchUserSubscriptions(userId, supabaseClient) {
  const supabase = supabaseClient || createSupabaseClient();
  const { data, error } = await supabase
    .from('user_subscriptions')
    .select('id, is_enabled, subscribed_at, unsubscribed_at, app:app_id(name, slug, is_active)')
    .eq('user_id', userId);

  if (error) throw error;
  return data || [];
}
