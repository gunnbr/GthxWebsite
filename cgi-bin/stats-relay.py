#!/usr/bin/env python

import cgi
import cgitb
import simplejson as json

debug = 0

if (debug == 1):
    print "Content-Type: text/html"
    print
    # Just to get good debug output
    cgitb.enable()
    print "<html><head><title>gthx factoid relay</title></head>"
    print "<body>"
    print "<H1>gthx factoid relay:</H1>"
else:
    print "Content-Type: text/json"
    print

import urllib

form = cgi.FieldStorage()

searchstring = None
con = None
response = None

queryurl = "http://ec2-52-10-64-85.us-west-2.compute.amazonaws.com/cgi-bin/stats.py"
query = urllib.urlopen(queryurl)
response = query.read()

if (debug == 1):
    print "<P>Success!</P>"

print response

if (debug == 1):
    print "</body></html>"


