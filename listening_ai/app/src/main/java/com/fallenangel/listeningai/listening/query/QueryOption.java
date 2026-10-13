package com.fallenangel.listeningai.listening.query;

/** One button in a question. */
public final class QueryOption {
    /** The candidate type this stands for, or "none". */
    public final String id;
    public final String label;

    public QueryOption(String id, String label) {
        this.id = id == null ? "none" : id;
        this.label = label == null ? "" : label;
    }

    public boolean isNone() {
        return "none".equals(id);
    }
}
