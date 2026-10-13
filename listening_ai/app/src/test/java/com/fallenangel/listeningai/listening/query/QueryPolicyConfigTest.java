package com.fallenangel.listeningai.listening.query;

import static org.junit.Assert.assertEquals;

import org.json.JSONObject;
import org.junit.Test;

/**
 * The policy's numbers, which are the part that will be tuned by listening.
 *
 * Two things matter and neither is the parsing: the defaults have to survive a
 * round trip (or a saved config silently changes the policy), and a config file
 * that only sets one field has to leave the others alone - a missing number
 * becoming zero means a threshold of zero, which is a policy that asks about
 * everything.
 */
public class QueryPolicyConfigTest {
    @Test
    public void theDefaultsSurviveARoundTrip() {
        QueryPolicyConfig read = QueryPolicyConfig.fromJson(
                QueryPolicyConfig.defaults().toJson());
        assertEquals(QueryPolicyConfig.defaults().highConfidenceThreshold,
                read.highConfidenceThreshold, 1e-9);
        assertEquals(QueryPolicyConfig.defaults().uncertaintyMargin,
                read.uncertaintyMargin, 1e-9);
        assertEquals(QueryPolicyConfig.defaults().cooldownMs, read.cooldownMs);
        assertEquals(QueryPolicyConfig.defaults().dismissLimit, read.dismissLimit);
        assertEquals(QueryPolicyConfig.defaults().maxQueriesPerMinute, read.maxQueriesPerMinute);
    }

    @Test
    public void aPartialConfigLeavesTheRestAtTheirDefaults() throws Exception {
        JSONObject only = new JSONObject().put("uncertaintyMargin", 0.2);
        QueryPolicyConfig read = QueryPolicyConfig.fromJson(only);
        assertEquals(0.2, read.uncertaintyMargin, 1e-9);
        assertEquals("and the others are still the defaults",
                QueryPolicyConfig.defaults().highConfidenceThreshold,
                read.highConfidenceThreshold, 1e-9);
        assertEquals(QueryPolicyConfig.defaults().dismissLimit, read.dismissLimit);
    }

    @Test
    public void noFileAtAllIsTheDefaults() {
        assertEquals(QueryPolicyConfig.defaults().cooldownMs,
                QueryPolicyConfig.fromJson(null).cooldownMs);
    }
}
