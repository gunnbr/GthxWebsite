#!/usr/bin/python

import sys
import cgi
import cgitb
import simplejson as json

debug = 0

if (debug == 1):
     sys.stdout.write("Content-Type: text/html\n\n")
     # Just to get good debug output
     cgitb.enable()
     sys.stdout.write("<html><head><title>gthx factoid relay</title></head>")
     sys.stdout.write("<body>")
     sys.stdout.write("<H1>gthx factoid relay:</H1>")
else:
     sys.stdout.write("Content-Type: text/json\n\n")

import urllib

form = cgi.FieldStorage()

reply = {}
reply["data"] = []

searchstring = None
con = None
response = None

if "searchstring" in form:
    searchstring = form["searchstring"].value

    queryurl = "http://ec2-52-10-64-85.us-west-2.compute.amazonaws.com/cgi-bin/factoids.py?searchstring=%s" % searchstring
    query = urllib.urlopen(queryurl)
    response = query.read()

    if (debug == 1):
        sys.stdout.write("<P>Success!</P>")

    reply["status"] = "success"

else:
    reply["status"] = "failure"
    reply["reason"] = "No search string."

sys.stdout.write(response)

if (debug == 1):
    sys.stdout.write("</body></html>")
